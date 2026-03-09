namespace Lis.Core.Channel;

/// <summary>Raw media downloaded from the messaging platform.</summary>
public sealed record MediaDownload(byte[] Data, string MimeType);
