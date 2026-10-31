using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace OdinVS.LanguageServer;

internal static class OdinContentType
{
    public const string Name = "odin";

    // MEF reads these static fields by attribute; they are never assigned.
#pragma warning disable CS0649
    [Export]
    [Name(Name)]
    [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    internal static ContentTypeDefinition? Definition;

    [Export]
    [FileExtension(".odin")]
    [ContentType(Name)]
    internal static FileExtensionToContentTypeDefinition? FileExtension;
#pragma warning restore CS0649
}
