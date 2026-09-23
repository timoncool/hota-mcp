using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HotaMcp;

/// A refusal before anything reached the game. The code names the reason in a form a harness can
/// count across a whole run, the message says it in words the agent acts on.
public sealed class ActionRefused(string code,string message):InvalidOperationException(message)
{
    public string Code{get;}=code;

    public const string StaleRevision="STALE_REVISION";
    public const string OperationReused="OPERATION_REUSED";
    public const string MeleeUnreachable="MELEE_UNREACHABLE";
    public const string HexNotAdjacent="HEX_NOT_ADJACENT";
    public const string HexUnreachable="HEX_UNREACHABLE";
    public const string SegmentNotTarget="SEGMENT_NOT_TARGET";
    public const string BadText="BAD_TEXT";
    public const string UnknownControl="UNKNOWN_CONTROL";
}

internal static class ToolErrors
{
    /// Without this filter the SDK answers every failed tool with a bare «An error occurred invoking»,
    /// so the agent never learns that its revision was stale or that the target is out of reach.
    public static McpRequestHandler<CallToolRequestParams,CallToolResult> Filter(McpRequestHandler<CallToolRequestParams,CallToolResult> next)
        =>async(context,cancellationToken)=>
        {
            try{return await next(context,cancellationToken);}
            catch(Exception e)when(e is not OperationCanceledException and not McpException)
            {
                string text=e is ActionRefused refused?$"[{refused.Code}] {refused.Message}":e.Message;
                return new CallToolResult{IsError=true,Content=[new TextContentBlock{Text=text}]};
            }
        };
}
