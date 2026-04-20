using ClosedXML.Excel;
using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

public class ThailandAddressSeedService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ThailandAddressSeedService> _logger;
    private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment _env;

    public ThailandAddressSeedService(
        AppDbContext context,
        IConfiguration configuration,
        ILogger<ThailandAddressSeedService> logger,
        Microsoft.AspNetCore.Hosting.IWebHostEnvironment env)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _env = env;
    }

    public async Task SeedAsync()
    {
        try
        {
            // await _context.Database.EnsureCreatedAsync(); // AppDbContext is handled in Program.cs
            var keyNames = _context.Model.FindEntityType(typeof(ThailandAddress))?.FindPrimaryKey()?.Properties.Select(x => x.Name);
            _logger.LogWarning($"[DEBUG] EF Core thinks the Primary Key for ThailandAddress is: {string.Join(", ", keyNames ?? new[] { "NULL" })}");

            // 1. Get from config
            string excelPathFromConfig = _configuration["SeedSettings:ThailandAddressExcelPath"] ?? "ThepExcel-Thailand-Tambon.xlsx";
            
            // 2. Resolve Path (If relative, make it relative to ContentRootPath)
            string excelPath = excelPathFromConfig;
            if (!Path.IsPathRooted(excelPath))
            {
                excelPath = Path.Combine(_env.ContentRootPath, excelPath);
            }
            
            if (!File.Exists(excelPath))
            {
                _logger.LogWarning($"[SEED ERROR] Excel file NOT FOUND at: {excelPath}");
                _logger.LogWarning($"Please place 'ThepExcel-Thailand-Tambon.xlsx' in the application root or update 'SeedSettings:ThailandAddressExcelPath' in appsettings.json");
                return;
            }

            _logger.LogWarning($"[SEED INFO] Reading Excel file: {excelPath}");
            using var workbook = new XLWorkbook(excelPath);
            var worksheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("postcode", StringComparison.OrdinalIgnoreCase));
            if (worksheet == null)
            {
                _logger.LogWarning("[SEED ERROR] Worksheet 'postcode' (case-insensitive) NOT FOUND!");
                return;
            }

            var headerRow = worksheet.Row(1);
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var foundHeaders = new List<string>();
            for (int i = 1; i <= (worksheet.LastColumnUsed()?.ColumnNumber() ?? 0); i++)
            {
                var val = headerRow.Cell(i).GetValue<string>().Trim();
                if (!string.IsNullOrEmpty(val)) 
                {
                    colMap[val] = i;
                    foundHeaders.Add(val);
                }
            }
            _logger.LogWarning($"[SEED INFO] Found columns: {string.Join(", ", foundHeaders)}");

            // Mapping based on your screenshot
            string? GetCol(params string[] aliases) => aliases.FirstOrDefault(a => colMap.ContainsKey(a));
            
            string colPostcode = GetCol("PostCode", "postcode");
            string colTambonId = GetCol("TambonID", "tambonid");
            string colSubDistrict = GetCol("TambonThaiShort", "tambonthaishort");
            string colDistrict = GetCol("DistrictThaiShort", "districtthaishort", "districttahshort");
            string colProvince = GetCol("ProvinceThai", "provinceThai", "ptovinceThai");

            if (colPostcode == null || colTambonId == null || colSubDistrict == null || colDistrict == null || colProvince == null)
            {
                _logger.LogWarning("[SEED ERROR] Missing required columns!");
                if (colPostcode == null) Console.WriteLine("- PostCode");
                if (colTambonId == null) Console.WriteLine("- TambonID");
                if (colSubDistrict == null) Console.WriteLine("- TambonThaiShort");
                if (colDistrict == null) Console.WriteLine("- DistrictThaiShort");
                if (colProvince == null) Console.WriteLine("- ProvinceThai");
                return;
            }

            var rows = worksheet.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<ClosedXML.Excel.IXLRangeRow>(); // Skip header
            var addresses = new List<ThailandAddress>();

            foreach (var row in rows)
            {
                var tId = row.Cell(colMap[colTambonId]).GetValue<string>().Trim();
                if (string.IsNullOrEmpty(tId)) continue;

                addresses.Add(new ThailandAddress
                {
                    tambonID = tId,
                    subDistrict = row.Cell(colMap[colSubDistrict]).GetValue<string>().Trim(),
                    district = row.Cell(colMap[colDistrict]).GetValue<string>().Trim(),
                    province = row.Cell(colMap[colProvince]).GetValue<string>().Trim(),
                    postcode = row.Cell(colMap[colPostcode]).GetValue<string>().Trim()
                });
            }

            if (addresses.Any())
            {
                _logger.LogWarning($"[SEED INFO] Preparing to RECREATE table and insert {addresses.Count} records...");
                
                // Drop and Recreate to ensure schema is updated
                await _context.Database.ExecuteSqlRawAsync(@"
                    IF OBJECT_ID('mbw.ThailandAddress', 'U') IS NOT NULL DROP TABLE mbw.ThailandAddress;
                    
                    CREATE TABLE [mbw].[ThailandAddress] (
                        [Id] INT IDENTITY(1,1) NOT NULL,
                        [tambonID] NVARCHAR(200) NOT NULL,
                        [subDistrict] NVARCHAR(200) NOT NULL,
                        [district] NVARCHAR(200) NOT NULL,
                        [province] NVARCHAR(200) NOT NULL,
                        [postcode] NVARCHAR(20) NOT NULL,
                        CONSTRAINT [PK_ThailandAddress] PRIMARY KEY ([Id])
                    );

                    CREATE INDEX [IX_ThailandAddress_province] ON [mbw].[ThailandAddress] ([province]);
                    CREATE INDEX [IX_ThailandAddress_district] ON [mbw].[ThailandAddress] ([district]);
                    CREATE INDEX [IX_ThailandAddress_tambonID] ON [mbw].[ThailandAddress] ([tambonID]);
                ");

                _logger.LogWarning("[SEED INFO] Inserting into SQL Server...");
                await _context.ThailandAddresses.AddRangeAsync(addresses);
                await _context.SaveChangesAsync();
                _logger.LogWarning($"[SEED SUCCESS] Successfully seeded {addresses.Count} records.");
            }
            else
            {
                Console.WriteLine("[SEED WARNING] No records found in the Excel sheet.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SEED FATAL ERROR] {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null) 
                Console.WriteLine($"[INNER] {ex.InnerException.Message}");
            _logger.LogError(ex, "Error seeding Thailand address data.");
        }
    }
}
