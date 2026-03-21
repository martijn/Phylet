using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Phylet.Data.Library;
using Phylet.Services;

namespace Phylet.Controllers;

[ApiController]
public sealed class ConnectionManagerController(
    EventSubscriptionService subscriptions,
    ILogger<ConnectionManagerController> logger) : ControllerBase
{
    private static readonly XNamespace SoapEnvNs = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace ConnectionManagerNs = "urn:schemas-upnp-org:service:ConnectionManager:1";
    private static readonly XNamespace ControlNs = "urn:schemas-upnp-org:control-1-0";

    private static readonly string SourceProtocolInfo = string.Join(",",
        LibraryAudioFormats.All.Select(format => $"http-get:*:{format.MimeType}:{format.ProtocolInfoFeatures}"));

    [HttpGet("/ConnectionManager/scpd.xml")]
    public ContentResult Scpd()
    {
        const string scpd = """
                            <?xml version="1.0"?>
                            <scpd xmlns="urn:schemas-upnp-org:service-1-0">
                              <specVersion><major>1</major><minor>0</minor></specVersion>
                              <actionList>
                                <action><name>GetProtocolInfo</name></action>
                                <action><name>GetCurrentConnectionIDs</name></action>
                                <action><name>GetCurrentConnectionInfo</name></action>
                              </actionList>
                            </scpd>
                            """;

        return Content(scpd, "application/xml; charset=utf-8");
    }

    [HttpPost("/upnp/control/connectionmanager")]
    public ContentResult Control()
    {
        var soapAction = Request.Headers["SOAPACTION"].FirstOrDefault()?.Trim('"') ?? string.Empty;

        return soapAction.EndsWith("#GetProtocolInfo", StringComparison.OrdinalIgnoreCase)
            ? SoapResponse("GetProtocolInfoResponse",
                new XElement("Source", SourceProtocolInfo),
                new XElement("Sink", string.Empty))
            : soapAction.EndsWith("#GetCurrentConnectionIDs", StringComparison.OrdinalIgnoreCase)
                ? SoapResponse("GetCurrentConnectionIDsResponse", new XElement("ConnectionIDs", "0"))
                : soapAction.EndsWith("#GetCurrentConnectionInfo", StringComparison.OrdinalIgnoreCase)
                    ? SoapResponse("GetCurrentConnectionInfoResponse",
                        new XElement("RcsID", "-1"),
                        new XElement("AVTransportID", "-1"),
                        new XElement("ProtocolInfo", "http-get:*:*:*"),
                        new XElement("PeerConnectionManager", string.Empty),
                        new XElement("PeerConnectionID", "-1"),
                        new XElement("Direction", "Output"),
                        new XElement("Status", "OK"))
                    : SoapFault(401, "Invalid Action");
    }

    [AcceptVerbs("SUBSCRIBE", Route = "/upnp/event/connectionmanager")]
    public IActionResult Subscribe()
    {
        var sidHeader = Request.Headers["SID"].FirstOrDefault();
        var timeoutHeader = Request.Headers["TIMEOUT"].FirstOrDefault();
        var result = subscriptions.Subscribe("ConnectionManager", sidHeader, timeoutHeader);
        if (!result.Success)
        {
            logger.LogWarning("ConnectionManager SUBSCRIBE renewal rejected for unknown SID={Sid}", result.Sid);
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        Response.Headers["SID"] = result.Sid;
        Response.Headers["TIMEOUT"] = $"Second-{result.TimeoutSeconds}";
        logger.LogInformation("ConnectionManager SUBSCRIBE accepted SID={Sid} Renewal={Renewal}", result.Sid, result.IsRenewal);
        return StatusCode(StatusCodes.Status200OK);
    }

    [AcceptVerbs("UNSUBSCRIBE", Route = "/upnp/event/connectionmanager")]
    public IActionResult Unsubscribe()
    {
        var sid = Request.Headers["SID"].FirstOrDefault();
        var removed = subscriptions.Unsubscribe(sid);
        if (!removed)
        {
            logger.LogWarning("ConnectionManager UNSUBSCRIBE rejected SID={Sid}", sid ?? string.Empty);
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        logger.LogInformation("ConnectionManager UNSUBSCRIBE accepted SID={Sid}", sid ?? string.Empty);
        return StatusCode(StatusCodes.Status200OK);
    }

    private static ContentResult SoapResponse(string responseName, params XElement[] values)
    {
        var response = new XDocument(
            new XElement(SoapEnvNs + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", SoapEnvNs),
                new XAttribute(SoapEnvNs + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
                new XElement(SoapEnvNs + "Body",
                    new XElement(ConnectionManagerNs + responseName,
                        new XAttribute(XNamespace.Xmlns + "u", ConnectionManagerNs),
                        values))));

        return new ContentResult
        {
            Content = response.ToString(SaveOptions.DisableFormatting),
            ContentType = "text/xml; charset=utf-8",
            StatusCode = StatusCodes.Status200OK
        };
    }

    private static ContentResult SoapFault(int errorCode, string errorDescription)
    {
        var fault = new XDocument(
            new XElement(SoapEnvNs + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", SoapEnvNs),
                new XElement(SoapEnvNs + "Body",
                    new XElement(SoapEnvNs + "Fault",
                        new XElement("faultcode", "s:Client"),
                        new XElement("faultstring", "UPnPError"),
                        new XElement("detail",
                            new XElement(ControlNs + "UPnPError",
                                new XElement(ControlNs + "errorCode", errorCode),
                                new XElement(ControlNs + "errorDescription", errorDescription)))))));

        return new ContentResult
        {
            Content = fault.ToString(SaveOptions.DisableFormatting),
            ContentType = "text/xml; charset=utf-8",
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }
}
