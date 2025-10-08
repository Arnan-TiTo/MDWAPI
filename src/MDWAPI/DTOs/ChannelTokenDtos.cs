namespace MDWAPI.Dtos
{
    public class ChannelTokenDtos
    {
        //public int Id { get; set; } = 0;
        public string Channel { get; set; } = null!;
        public string Environment { get; set; } = null!;
        public string? AuthType { get; set; }

        public long? PartnerId { get; set; }
        public string? AppKey { get; set; }

        public long? AccountIdBig { get; set; }
        public string? AccountIdStr { get; set; }

        public string AccessToken { get; set; } = null!;
        public string? RefreshToken { get; set; }
        public DateTime AccessTokenExpAt { get; set; }
        public DateTime? RefreshTokenExpAt { get; set; }

        public string? Scope { get; set; }
        public string? Country { get; set; }
        public string? Region { get; set; }

        public int? CompanysId { get; set; }
        public int? PartnersId { get; set; }

        public string? TokenPayloadJson { get; set; }
        public string? ExtraJson { get; set; }

        public bool isActive { get; set; }
    }
}
