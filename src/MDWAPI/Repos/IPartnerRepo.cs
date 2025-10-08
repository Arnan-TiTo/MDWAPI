using MDWAPI.Dtos;

namespace MDWAPI.Repos;

// อ่านค่า PartnerConfig จากตาราง Partners ด้วย PK (Partners.Id)

public interface IPartnerRepo
{
    Task<PartnerConfigDtos?> GetConfigByPartnersIdAsync(int partnersId, CancellationToken ct);
}
