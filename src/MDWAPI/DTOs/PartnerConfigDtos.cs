public record class PartnerConfigDtos
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public int? CompanysId { get; init; }
    public int? PartnerId { get; init; }
    public string? PartnerKey { get; init; }
    public string? AppKey { get; init; }
    public string? Environment { get; init; }
}
