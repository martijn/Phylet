using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Phylet.Data.Configuration;
using Phylet.Services;

namespace Phylet.Controllers;

[ApiController]
public sealed class DeviceDescriptionController(IDeviceConfigurationProvider configurationProvider) : ControllerBase
{
    private static readonly XNamespace DeviceNs = "urn:schemas-upnp-org:device-1-0";

    [HttpGet("/description.xml")]
    public ContentResult Get()
    {
        var baseUrl = GetBaseUrl();
        var xml = BuildDescription(baseUrl, configurationProvider.Current);
        return Content(xml, "application/xml; charset=utf-8");
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(Request.Scheme) && Request.Host.HasValue)
        {
            return $"{Request.Scheme}://{Request.Host.Value}";
        }

        return "http://localhost";
    }

    private static string BuildDescription(string baseUrl, RuntimeDeviceConfiguration configuration)
    {
        var doc = new XDocument(
            new XElement(DeviceNs + "root",
                new XElement(DeviceNs + "specVersion",
                    new XElement(DeviceNs + "major", "1"),
                    new XElement(DeviceNs + "minor", "0")),
                new XElement(DeviceNs + "device",
                    new XElement(DeviceNs + "deviceType", "urn:schemas-upnp-org:device:MediaServer:1"),
                    new XElement(DeviceNs + "friendlyName", configuration.FriendlyName),
                    new XElement(DeviceNs + "manufacturer", configuration.Manufacturer),
                    new XElement(DeviceNs + "modelName", configuration.ModelName),
                    new XElement(DeviceNs + "UDN", configuration.DeviceUuid),
                    new XElement(DeviceNs + "iconList",
                        IconCatalog.DeviceDescriptionIcons.Select(icon =>
                            new XElement(DeviceNs + "icon",
                                new XElement(DeviceNs + "mimetype", icon.MimeType),
                                new XElement(DeviceNs + "width", icon.Width),
                                new XElement(DeviceNs + "height", icon.Height),
                                new XElement(DeviceNs + "depth", icon.Depth),
                                new XElement(DeviceNs + "url", icon.Url)))),
                    new XElement(DeviceNs + "serviceList",
                        new XElement(DeviceNs + "service",
                            new XElement(DeviceNs + "serviceType", "urn:schemas-upnp-org:service:ContentDirectory:1"),
                            new XElement(DeviceNs + "serviceId", "urn:upnp-org:serviceId:ContentDirectory"),
                            new XElement(DeviceNs + "SCPDURL", "/ContentDirectory/scpd.xml"),
                            new XElement(DeviceNs + "controlURL", "/upnp/control/contentdirectory"),
                            new XElement(DeviceNs + "eventSubURL", "/upnp/event/contentdirectory")),
                        new XElement(DeviceNs + "service",
                            new XElement(DeviceNs + "serviceType", "urn:schemas-upnp-org:service:ConnectionManager:1"),
                            new XElement(DeviceNs + "serviceId", "urn:upnp-org:serviceId:ConnectionManager"),
                            new XElement(DeviceNs + "SCPDURL", "/ConnectionManager/scpd.xml"),
                            new XElement(DeviceNs + "controlURL", "/upnp/control/connectionmanager"),
                            new XElement(DeviceNs + "eventSubURL", "/upnp/event/connectionmanager"))),
                    new XElement(DeviceNs + "presentationURL", baseUrl))));

        return doc.ToString(SaveOptions.DisableFormatting);
    }
}
