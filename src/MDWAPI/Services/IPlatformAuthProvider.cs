using MDWAPI.Common;

namespace MDWAPI.Services;

public interface IPlatformAuthProvider
{
    // แลกโค้ดเป็น access/refresh token และบันทึกลง ChannelTokens
    // platform คือแพลตฟอร์มเป้าหมาย
    // partnersId คือ PK ของตาราง Partners
    // accountIdBig หรือ accountIdStr ใช้ระบุร้านตามแพลตฟอร์ม
    // code คือ authorization code จากฝั่ง FE
    Task<object> ExchangeCodeAsync(
        Platform platform,
        int partnersId,
        long? accountIdBig,
        string? accountIdStr,
        string code,
        CancellationToken ct);

    // รีเฟรชโทเค็นและบันทึกลง ChannelTokens
    // refreshToken คือค่า refresh token ล่าสุด
    Task<object> RefreshAsync(
        Platform platform,
        int partnersId,
        long? accountIdBig,
        string? accountIdStr,
        string refreshToken,
        CancellationToken ct);
}
