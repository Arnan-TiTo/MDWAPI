namespace MDWAPI.Repos
{
    public interface IPartnerRepo
    {
        Task<PartnerConfigDtos?> GetConfigByPartnersIdAsync(int partnersId, CancellationToken ct);
    }
}
