using System.Text.Json;
using ModelContextProtocol.Server;

namespace HotaMcp;

/// <summary>
/// Documentation exposed to MCP clients as URI-addressable resources, next to the action tools.
/// Agents can list and read documentation directly; the tools (hota_docs_*) cover search.
/// </summary>
[McpServerResourceType]
public sealed class GameResources(IGameEndpoint endpoint)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    [McpServerResource(UriTemplate="hota://docs/index",Name="Documentation index",Title="Hota MCP documentation index",MimeType="application/json")]
    public async Task<string> DocumentationIndex(CancellationToken cancellationToken)
    {
        var catalog=await endpoint.DocsCatalog(cancellationToken);
        return JsonSerializer.Serialize(catalog,Json);
    }

    [McpServerResource(UriTemplate="hota://docs/{path}",Name="Documentation file",Title="Hota MCP documentation file",MimeType="text/markdown")]
    public async Task<string> DocumentationFile(string path,string? heading,CancellationToken cancellationToken)
    {
        // Nested paths arrive as one segment with "~" in place of the directory separator.
        var result=await endpoint.DocsRead(path.Replace('~','/'),heading,cancellationToken);
        return result.Found?result.Text:"Documentation not found: "+(result.Note??result.Path);
    }
}
