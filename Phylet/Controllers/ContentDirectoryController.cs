using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Phylet.Data.Library;
using Phylet.Services;

namespace Phylet.Controllers;

[ApiController]
public sealed class ContentDirectoryController(
    LibraryService library,
    DidlBuilder didlBuilder,
    EventSubscriptionService subscriptions,
    ILogger<ContentDirectoryController> logger) : ControllerBase
{
    private static readonly XNamespace SoapEnvNs = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace ContentDirectoryNs = "urn:schemas-upnp-org:service:ContentDirectory:1";
    private static readonly XNamespace ControlNs = "urn:schemas-upnp-org:control-1-0";

    [HttpGet("/ContentDirectory/scpd.xml")]
    public ContentResult Scpd()
    {
        const string scpd = """
                            <?xml version="1.0"?>
                            <scpd xmlns="urn:schemas-upnp-org:service-1-0">
                              <specVersion><major>1</major><minor>0</minor></specVersion>
                              <actionList>
                                <action><name>Browse</name></action>
                                <action><name>GetSearchCapabilities</name></action>
                                <action><name>GetSortCapabilities</name></action>
                                <action><name>GetSystemUpdateID</name></action>
                              </actionList>
                            </scpd>
                            """;

        return Content(scpd, "application/xml; charset=utf-8");
    }

    [HttpPost("/upnp/control/contentdirectory")]
    public async Task<ContentResult> Control()
    {
        var bodyText = await new StreamReader(Request.Body).ReadToEndAsync();
        var actionName = ResolveActionName(Request.Headers["SOAPACTION"].FirstOrDefault(), bodyText);

        return actionName.Equals("Browse", StringComparison.OrdinalIgnoreCase)
            ? await HandleBrowseAsync(bodyText)
            : actionName.Equals("GetSearchCapabilities", StringComparison.OrdinalIgnoreCase)
                ? SoapResponse("GetSearchCapabilitiesResponse", new XElement("SearchCaps", string.Empty))
                : actionName.Equals("GetSortCapabilities", StringComparison.OrdinalIgnoreCase)
                    ? SoapResponse("GetSortCapabilitiesResponse", new XElement("SortCaps", string.Empty))
                    : actionName.Equals("GetSystemUpdateID", StringComparison.OrdinalIgnoreCase)
                        ? SoapResponse("GetSystemUpdateIDResponse", new XElement("Id", "1"))
                        : HandleUnknownAction(actionName, bodyText);
    }

    [AcceptVerbs("SUBSCRIBE", Route = "/upnp/event/contentdirectory")]
    public IActionResult Subscribe()
    {
        var sidHeader = Request.Headers["SID"].FirstOrDefault();
        var timeoutHeader = Request.Headers["TIMEOUT"].FirstOrDefault();
        var result = subscriptions.Subscribe("ContentDirectory", sidHeader, timeoutHeader);
        if (!result.Success)
        {
            logger.LogWarning("ContentDirectory SUBSCRIBE renewal rejected for unknown SID={Sid}", result.Sid);
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        Response.Headers["SID"] = result.Sid;
        Response.Headers["TIMEOUT"] = $"Second-{result.TimeoutSeconds}";
        logger.LogInformation("ContentDirectory SUBSCRIBE accepted SID={Sid} Renewal={Renewal}", result.Sid, result.IsRenewal);
        return StatusCode(StatusCodes.Status200OK);
    }

    [AcceptVerbs("UNSUBSCRIBE", Route = "/upnp/event/contentdirectory")]
    public IActionResult Unsubscribe()
    {
        var sid = Request.Headers["SID"].FirstOrDefault();
        var removed = subscriptions.Unsubscribe(sid);
        if (!removed)
        {
            logger.LogWarning("ContentDirectory UNSUBSCRIBE rejected SID={Sid}", sid ?? string.Empty);
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        logger.LogInformation("ContentDirectory UNSUBSCRIBE accepted SID={Sid}", sid ?? string.Empty);
        return StatusCode(StatusCodes.Status200OK);
    }

    private async Task<ContentResult> HandleBrowseAsync(string requestBody)
    {
        try
        {
            var doc = XDocument.Parse(requestBody);
            var objectId = FindArgument(doc, "ObjectID") ?? "0";
            var browseFlag = FindArgument(doc, "BrowseFlag") ?? "BrowseDirectChildren";
            var startingIndexRaw = FindArgument(doc, "StartingIndex") ?? "0";
            var requestedCountRaw = FindArgument(doc, "RequestedCount") ?? "0";

            if (!IsSupportedBrowseFlag(browseFlag))
            {
                return SoapFault(402, "Invalid Args");
            }

            if (!TryParseNonNegativeInt(startingIndexRaw, out var startingIndex)
                || !TryParseNonNegativeInt(requestedCountRaw, out var requestedCount))
            {
                return SoapFault(402, "Invalid Args");
            }

            var browseResult = await library.BrowseAsync(
                objectId,
                browseFlag,
                startingIndex,
                requestedCount,
                HttpContext.RequestAborted);
            if (browseResult.Status is LibraryBrowseStatus.NoSuchObject)
            {
                return SoapFault(701, "No Such Object");
            }

            if (browseResult.Status is LibraryBrowseStatus.NoSuchContainer)
            {
                return SoapFault(710, "No Such Container");
            }

            var baseUri = new Uri($"{Request.Scheme}://{Request.Host.Value}/");
            var result = didlBuilder.BuildBrowse(browseResult, baseUri);

            return SoapResponse("BrowseResponse",
                new XElement("Result", result.ResultXml),
                new XElement("NumberReturned", result.NumberReturned),
                new XElement("TotalMatches", result.TotalMatches),
                new XElement("UpdateID", result.UpdateId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse Browse request");
            return SoapFault(402, "Invalid Args");
        }
    }

    private ContentResult HandleUnknownAction(string actionName, string bodyText)
    {
        var preview = bodyText.Length > 512 ? bodyText[..512] + "..." : bodyText;
        logger.LogWarning(
            "Unsupported ContentDirectory action {ActionName}. SOAP body preview: {BodyPreview}",
            actionName,
            preview);
        return SoapFault(401, "Invalid Action");
    }

    private static string ResolveActionName(string? soapActionHeader, string bodyText)
    {
        var header = soapActionHeader?.Trim('"');
        if (!string.IsNullOrWhiteSpace(header))
        {
            var idx = header.LastIndexOf('#');
            if (idx >= 0 && idx + 1 < header.Length)
            {
                return header[(idx + 1)..];
            }

            return header;
        }

        try
        {
            var doc = XDocument.Parse(bodyText);
            var bodyNode = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Body", StringComparison.OrdinalIgnoreCase));
            var actionNode = bodyNode?.Elements().FirstOrDefault();
            return actionNode?.Name.LocalName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? FindArgument(XDocument doc, string name)
    {
        var node = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        return node?.Value;
    }

    private static bool IsSupportedBrowseFlag(string browseFlag) =>
        browseFlag.Equals("BrowseMetadata", StringComparison.OrdinalIgnoreCase)
        || browseFlag.Equals("BrowseDirectChildren", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseNonNegativeInt(string value, out int parsed)
    {
        if (int.TryParse(value, out parsed) && parsed >= 0)
        {
            return true;
        }

        parsed = 0;
        return false;
    }

    private static ContentResult SoapResponse(string responseName, params XElement[] values)
    {
        var response = new XDocument(
            new XElement(SoapEnvNs + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", SoapEnvNs),
                new XAttribute(SoapEnvNs + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
                new XElement(SoapEnvNs + "Body",
                    new XElement(ContentDirectoryNs + responseName,
                        new XAttribute(XNamespace.Xmlns + "u", ContentDirectoryNs),
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
