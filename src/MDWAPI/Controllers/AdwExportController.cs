using ClosedXML.Excel;
using Dapper;
using MDWAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Linq;
using System.Text;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/adw/export")]
    public class AdwExportController : ControllerBase
    {
        private readonly AppDbContext _db;
        public AdwExportController(AppDbContext db) => _db = db;


        /// <summary>
        /// ตรวจสอบจำนวนข้อมูลทั้งหมด (total rows), จำนวนหน้า (total pages)
        /// และเช็คว่า page ที่ขออยู่ในช่วงที่มีข้อมูลหรือไม่
        /// </summary>
        [HttpGet("flowaccount/ordersmeta")]
        public async Task<IActionResult> GetFlowAccountOrdersMeta(
            [FromQuery] string? channel = null,
            [FromQuery] long shopId = 0,
            [FromQuery] DateTime? createdFrom = null,
            [FromQuery] DateTime? createdTo = null,
            [FromQuery] DateTime? updatedFrom = null,
            [FromQuery] DateTime? updatedTo = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 500,
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 5000) pageSize = 500;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var where = new StringBuilder(@"
            WHERE 1=1 ");

            if (!string.IsNullOrWhiteSpace(channel))
                where.Append(" AND channel = @channel");

            if (shopId > 0)
                where.Append(" AND shop_id = @shopId");

            if (createdFrom.HasValue) where.Append(" AND created_at_th >= @createdFrom");
            if (createdTo.HasValue) where.Append(" AND created_at_th <  @createdToPlus");
            if (updatedFrom.HasValue) where.Append(" AND updated_at_th >= @updatedFrom");
            if (updatedTo.HasValue) where.Append(" AND updated_at_th <  @updatedToPlus");

            var sqlCount = $@"
                            SELECT COUNT(1)
                            FROM adw.vw_OrderExportFormatTH
                            {where};";

            DateTime? createdToPlus = createdTo?.Date.AddDays(1);
            DateTime? updatedToPlus = updatedTo?.Date.AddDays(1);

            var p = new
            {
                channel,
                shopId,
                createdFrom,
                createdToPlus,
                updatedFrom,
                updatedToPlus
            };

            var totalRows = await conn.ExecuteScalarAsync<long>(sqlCount, p);

            var totalPages = totalRows == 0
                ? 0
                : (int)Math.Ceiling(totalRows / (double)pageSize);

            var lastPage = totalPages; // ถ้าไม่มีข้อมูล = 0

            // page ที่ขอ "เกิน" ช่วงที่มีข้อมูลหรือไม่
            // หมายเหตุ: ถ้า totalPages = 0 ให้ถือว่า page ใดๆ ก็ out of range
            var isOutOfRange = totalPages == 0 ? true : page > lastPage;

            // คำนวณ hasPrev/hasNext แบบปลอดภัย
            var hasPrev = totalPages > 0 && page > 1 && page <= lastPage;
            var hasNext = totalPages > 0 && page < lastPage;

            // ตัวช่วย: ช่วง row index (1-based) ที่ page นี้จะครอบคลุม (ถ้า valid)
            long? fromRow = null;
            long? toRow = null;
            if (!isOutOfRange)
            {
                fromRow = ((long)(page - 1) * pageSize) + 1;
                toRow = Math.Min((long)page * pageSize, totalRows);
            }

            return Ok(new
            {
                totalRows,
                page,
                pageSize,
                totalPages,
                lastPage,
                isOutOfRange,
                hasPrev,
                hasNext,
                fromRow,
                toRow
            });
        }

        /// <summary>
        /// คืนข้อมูลทุกฟิลด์จาก adw.vw_OrderExportFormatTH (แบ่งหน้า) สำหรับ FlowAccount (JSON)
        /// </summary>
        [HttpGet("flowaccount/orders")]
        public async Task<IActionResult> GetFlowAccountOrders(
            [FromQuery] string? channel = null,
            [FromQuery] long shopId = 0,
            [FromQuery] DateTime? createdFrom = null,
            [FromQuery] DateTime? createdTo = null,
            [FromQuery] DateTime? updatedFrom = null,
            [FromQuery] DateTime? updatedTo = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 500,
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 5000) pageSize = 500;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var where = new StringBuilder(@"
            WHERE 1=1 ");

            if (!string.IsNullOrWhiteSpace(channel))
                where.Append(" AND channel = @channel");

            if (shopId > 0)
                where.Append(" AND shop_id = @shopId");

            if (createdFrom.HasValue) where.Append(" AND created_at_th >= @createdFrom");
            if (createdTo.HasValue) where.Append(" AND created_at_th <  @createdToPlus");
            if (updatedFrom.HasValue) where.Append(" AND updated_at_th >= @updatedFrom");
            if (updatedTo.HasValue) where.Append(" AND updated_at_th <  @updatedToPlus");

            var sqlCount = $@"
                            SELECT COUNT(1)
                            FROM adw.vw_OrderExportFormatTH
                            {where};";

            var sqlData = $@"
                            SELECT *
                            FROM adw.vw_OrderExportFormatTH
                            {where}
                            ORDER BY created_at_th, order_no, qty_sold
                            OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY;";

            DateTime? createdToPlus = createdTo?.Date.AddDays(1);
            DateTime? updatedToPlus = updatedTo?.Date.AddDays(1);

            // params สำหรับ filter (ใช้กับ COUNT ด้วย)
            var pFilter = new
            {
                channel,
                shopId,
                createdFrom,
                createdToPlus,
                updatedFrom,
                updatedToPlus
            };

            // ใช้ long กัน overflow
            var totalRows = await conn.ExecuteScalarAsync<long>(sqlCount, pFilter);

            var totalPages = totalRows == 0
                ? 0
                : (int)Math.Ceiling(totalRows / (double)pageSize);

            var lastPage = totalPages; // 0 ถ้าไม่มีข้อมูล

            // ถือว่า out-of-range เฉพาะกรณีมีข้อมูลแล้ว page เกิน lastPage
            var isOutOfRange = totalPages > 0 && page > lastPage;

            var hasPrev = totalPages > 0 && page > 1 && page <= lastPage;
            var hasNext = totalPages > 0 && page < lastPage;

            // ถ้า page เกิน ให้ตอบกลับเป็น empty items (ไม่ยิง query data)
            if (isOutOfRange)
            {
                return Ok(new
                {
                    total = totalRows,
                    page,
                    pageSize,
                    totalPages,
                    lastPage,
                    isOutOfRange,
                    hasPrev,
                    hasNext,
                    items = Array.Empty<object>() // หรือ new List<object>()
                });
            }

            // params สำหรับดึงข้อมูล
            var pData = new
            {
                channel,
                shopId,
                createdFrom,
                createdToPlus,
                updatedFrom,
                updatedToPlus,
                offset = (page - 1) * pageSize,
                take = pageSize
            };

            var items = (await conn.QueryAsync(sqlData, pData)).ToList();

            return Ok(new
            {
                total = totalRows,
                page,
                pageSize,
                totalPages,
                lastPage,
                isOutOfRange,
                hasPrev,
                hasNext,
                items
            });
        }

        /// <summary>
        /// ดาวน์โหลด Excel (.xlsx) สำหรับ FlowAccount
        /// จะ "ไม่" export 3 ฟิลด์ท้าย: channel, shop_id, updated_at_th
        /// </summary>
        [HttpGet("flowaccount/orders.xlsx")]
        public async Task<IActionResult> DownloadFlowAccountOrdersXlsx(
            [FromQuery] string channel,
            [FromQuery] long shopId,
            [FromQuery] DateTime? createdFrom,
            [FromQuery] DateTime? createdTo,
            [FromQuery] DateTime? updatedFrom,
            [FromQuery] DateTime? updatedTo,
            CancellationToken ct = default)
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var where = new StringBuilder(@"
            WHERE channel = @channel AND shop_id = @shopId");

            if (createdFrom.HasValue) where.Append(" AND created_at_th >= @createdFrom");
            if (createdTo.HasValue) where.Append(" AND created_at_th <  @createdToPlus");
            if (updatedFrom.HasValue) where.Append(" AND updated_at_th >= @updatedFrom");
            if (updatedTo.HasValue) where.Append(" AND updated_at_th <  @updatedToPlus");

            var sql = $@"
            SELECT *
            FROM adw.vw_OrderExportFormatTH
            {where}
            ORDER BY created_at_th, order_no, qty_sold;";

            DateTime? createdToPlus = createdTo?.Date.AddDays(1);
            DateTime? updatedToPlus = updatedTo?.Date.AddDays(1);

            var rows = (await conn.QueryAsync(sql, new
            {
                channel,
                shopId,
                createdFrom,
                createdToPlus,
                updatedFrom,
                updatedToPlus
            })).ToList();

            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "channel", "shop_id", "updated_at_th" };

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Orders");

            if (rows.Count > 0)
            {
                var first = (IDictionary<string, object>)rows[0];
                var headers = first.Keys.Where(k => !blocked.Contains(k)).ToList();

                // Header
                for (int c = 0; c < headers.Count; c++)
                    ws.Cell(1, c + 1).SetValue(headers[c]);

                // Data
                int r = 2;
                foreach (var row in rows)
                {
                    var dict = (IDictionary<string, object>)row;
                    for (int c = 0; c < headers.Count; c++)
                    {
                        var key = headers[c];
                        dict.TryGetValue(key, out var v);
                        WriteCell(ws.Cell(r, c + 1), v);
                    }
                    r++;
                }

                ws.Columns().AdjustToContents();
                ws.SheetView.FreezeRows(1);
                ws.Row(1).Style.Font.SetBold();
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;

            var fileName = $"order-export-flowaccount_{channel}_{shopId}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // === Helpers ===

        private static string CsvEscape(object? value)
        {
            var s = value switch
            {
                null => "",
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss"),
                _ => value?.ToString() ?? ""
            };
            s = s.Replace("\"", "\"\"");
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                s = $"\"{s}\"";
            return s;
        }

        /// <summary>
        /// เขียนค่าใส่เซลล์ให้ตรงชนิด (แก้ปัญหา XLCellValue)
        /// </summary>
        private static void WriteCell(IXLCell cell, object? v)
        {
            switch (v)
            {
                case null:
                    cell.SetValue(string.Empty);
                    break;

                case DateTime dt:
                    cell.SetValue(dt);
                    cell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    break;

                case DateTimeOffset dto:
                    // ใช้ LocalDateTime/UTC ก็เลือกตามต้องการ; ที่นี่ใช้ Local (ไทย)
                    cell.SetValue(dto.LocalDateTime);
                    cell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    break;

                case bool b:
                    cell.SetValue(b);
                    break;

                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    // เขียนเป็นตัวเลขจำนวนเต็ม
                    cell.SetValue(Convert.ToInt64(v));
                    break;

                case float or double or decimal:
                    // เขียนเป็นตัวเลขทศนิยม
                    cell.SetValue(Convert.ToDouble(v));
                    break;

                default:
                    cell.SetValue(v.ToString() ?? string.Empty);
                    break;
            }
        }
    }
}
