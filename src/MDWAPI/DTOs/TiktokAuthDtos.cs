namespace MDWAPI.Dtos;

public sealed class TiktokTokenEnvelope
{
    public int Code { get; set; }
    public string? Message { get; set; }
    public TiktokTokenData? Data { get; set; }
}

public sealed class TiktokTokenData
{
    public string? Access_token { get; set; }
    public string? Refresh_token { get; set; }
    // บางเอกสารใช้ชื่อแตกต่าง: access_token_expire_in / expire_in
    public int? Access_token_expire_in { get; set; }
    public int? Expires_in { get; set; }
    public int? Refresh_token_expire_in { get; set; }
    public long? Expire_at { get; set; } // บาง region ส่ง epoch มาเลย
}
