using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using CriterionArt.Configuration;

namespace CriterionArt;

public class Plugin : BasePlugin<PluginConfiguration>
{
    public override string Name => "Criterion Collection Art";

    public override Guid Id => Guid.Parse("8c052238-e202-49d1-8b6b-287430af58b5");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }
}