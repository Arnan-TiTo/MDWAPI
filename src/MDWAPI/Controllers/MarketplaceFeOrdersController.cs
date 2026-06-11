using MDWAPI.Data;
using MDWAPI.Entities;
using MDWAPI.Models;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/fe/orders")]
    public class MarketplaceFeOrdersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<MarketplaceFeOrdersController> _log;
        private readonly ShopeeOrderService _shopee;
        private readonly TiktokOrderService _tiktok;
        private readonly IUnifiedOrderWriter _writer;

        public MarketplaceFeOrdersController(
            AppDbContext db,
            ILogger<MarketplaceFeOrdersController> log,
            ShopeeOrderService shopee,
            TiktokOrderService tiktok,
            IUnifiedOrderWriter writer)
        {
            _db = db;
            _log = log;
            _shopee = shopee;
            _tiktok = tiktok;
            _writer = writer;
        }

        // ====== DTOs ======
        public sealed record UnifiedOrderListItemDto(
            long UnifiedOrderId,
            string? ExternalOrderNo,
            string? Channel,
            long? ShopId,
            string? SellerId,
            string? OrderStatus,
            DateTime? CreatedTimeUtc,
            DateTime? UpdatedTimeUtc,
            string? BuyerUserId,
            string? BuyerUsername,
            decimal? SubtotalAmount,
            decimal? DiscountSellerAmount,
            decimal? DiscountPlatformAmount,
            decimal? VoucherAmount,
            decimal? ShippingFeeAmount,
            decimal? TotalAmount,
            decimal? PaidAmount,
            string? ItemsJson,
            string? PaymentsJson,
            string? ShipmentsJson,
            string? ShipToJson,
            // Escrow / income breakdown
            decimal? EscrowAmount,
            decimal? BuyerPaidShippingFee,
            decimal? ActualShippingFee,
            decimal? PlatformShippingRebate,
            decimal? CommissionFee,
            decimal? ServiceFee,
            decimal? PlatformFee,
            decimal? PaymentTransactionFee,
            decimal? AmsCommissionFee,
            string? SellerVoucherCode,
            FlowAccountAmountsDto FlowAccountAmounts,
            ReconciliationAmountsDto ReconciliationAmounts,
            decimal? SellerVoucher,
            decimal? ShopeeVoucher,
            decimal? ShippingFeeSstAmount
        );

        public sealed record UnifiedReturnListItemDto(
            long UnifiedReturnId,
            long? UnifiedOrderId,
            string? Channel,
            long? ShopId,
            string? ExternalOrderId,
            string ExternalReturnId,
            string? ReturnStatus,
            string? ReturnReason,
            string? TextReason,
            string? ReturnType,
            string? ReturnSolution,
            string? NegotiationStatus,
            decimal? RefundAmount,
            string? Currency,
            string? ReturnItemsJson,
            string? ImagesJson,
            DateTime? CreatedAtUtc,
            DateTime? UpdatedAtUtc,
            DateTime IngestedAtUtc,
            string? RawJson
        );

        public sealed record FlowAccountAmountsDto(
            decimal GrossAmount,
            decimal SellerDiscountAmount,
            decimal NetAfterSellerDiscountAmount,
            decimal PlatformDiscountAmount,
            decimal AmountDue,
            decimal ShippingFeeAmount,
            decimal TaxAmount,
            decimal GrandTotalAmount
        );

        public sealed record ReconciliationAmountsDto(
            decimal EscrowAmount,
            decimal BuyerPaidShippingFee,
            decimal ActualShippingFee,
            decimal PlatformShippingRebate,
            decimal CommissionFee,
            decimal ServiceFee,
            decimal PlatformFee,
            decimal PaymentTransactionFee,
            decimal AmsCommissionFee,
            decimal TotalFeeAmount,
            decimal NetPayoutAmount,
            string? SellerVoucherCode
        );

        public sealed record PagedResult<T>(
            int TotalItems,
            int Page,
            int Size,
            IEnumerable<T> Items
        );

        // ====== OPTIONS (กัน 405 จาก preflight CORS) ======
        [HttpOptions]
        [AllowAnonymous]
        public IActionResult Options() => Ok();

        // ====== LIST (filter + paging) ======
        // GET /api/fe/orders?channel=TikTok&shopId=123&q=ABC&status=Paid&fromUtc=2025-10-01&toUtc=2025-10-22&page=1&pageSize=50&sort=createdDesc
        [HttpGet]
        public async Task<ActionResult<PagedResult<UnifiedOrderListItemDto>>> List(
            [FromQuery] string? channel,
            [FromQuery] long? shopId,
            [FromQuery] string? q,
            [FromQuery] string? status,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] string dateField = "updated",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? sort = "updatedDesc",
            CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 50;

            var qy = _db.VUnifiedOrders.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(channel))
                qy = qy.Where(o => o.Channel == channel);

            if (shopId is > 0)
                qy = qy.Where(o => o.ShopId == shopId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                qy = qy.Where(o => o.OrderStatus == status);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim();
                qy = qy.Where(o =>
                    (o.ExternalOrderNo ?? "").Contains(kw) ||
                    (o.SellerId ?? "").Contains(kw) ||
                    o.UnifiedOrderId.ToString().Contains(kw)
                );
            }

            if (fromUtc.HasValue)
            {
                qy = dateField.ToLowerInvariant() switch
                {
                    "updated" => qy.Where(o => o.UpdatedTimeUtc.HasValue && o.UpdatedTimeUtc.Value >= fromUtc.Value),
                    _ => qy.Where(o => o.CreatedTimeUtc >= fromUtc.Value)
                };
            }

            if (toUtc.HasValue)
            {
                qy = dateField.ToLowerInvariant() switch
                {
                    "updated" => qy.Where(o => o.UpdatedTimeUtc.HasValue && o.UpdatedTimeUtc.Value <= toUtc.Value),
                    _ => qy.Where(o => o.CreatedTimeUtc <= toUtc.Value)
                };
            }

            // sort
            qy = (sort ?? "").ToLowerInvariant() switch
            {
                "createdasc" => qy.OrderBy(o => o.CreatedTimeUtc).ThenBy(o => o.UnifiedOrderId),
                "createddesc" => qy.OrderByDescending(o => o.CreatedTimeUtc).ThenByDescending(o => o.UnifiedOrderId),
                "updatedasc" => qy.OrderBy(o => o.UpdatedTimeUtc).ThenBy(o => o.UnifiedOrderId),
                _ => qy.OrderByDescending(o => o.UpdatedTimeUtc).ThenByDescending(o => o.UnifiedOrderId),
            };

            var total = await qy.CountAsync(ct);

            var pageRows = await qy
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var data = pageRows.Select(ToListItemDto).ToList();

            // *** ตรงกับ PagedResult ของ FE ***
            return Ok(new PagedResult<UnifiedOrderListItemDto>(
                TotalItems: total,
                Page: page,
                Size: pageSize,
                Items: data
            ));
        }

        // ====== UNIFIED RETURNS (filter + paging) ======
        // GET /api/fe/orders/unifiedReturn?channel=Shopee&shopId=123&q=ABC&status=REQUESTED&page=1&pageSize=50
        [HttpGet("unifiedReturn")]
        public async Task<ActionResult<PagedResult<UnifiedReturnListItemDto>>> ListUnifiedReturns(
            [FromQuery] string? channel,
            [FromQuery] long? shopId,
            [FromQuery] string? q,
            [FromQuery] string? orderRef,
            [FromQuery] string? returnRef,
            [FromQuery] string? status,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] string dateField = "updated",
            [FromQuery] bool includeRaw = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? sort = "updatedDesc",
            CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 50;

            var qy = _db.UnifiedReturns.AsNoTracking().AsQueryable();

            qy = ApplyUnifiedReturnFilters(qy, channel, shopId, q, orderRef, returnRef, status, fromUtc, toUtc, dateField);
            qy = ApplyUnifiedReturnSort(qy, sort);

            var total = await qy.CountAsync(ct);

            var rows = await qy
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var data = rows.Select(r => ToReturnListItemDto(r, includeRaw)).ToList();

            return Ok(new PagedResult<UnifiedReturnListItemDto>(
                TotalItems: total,
                Page: page,
                Size: pageSize,
                Items: data
            ));
        }

        // GET /api/fe/orders/unifiedReturn/10
        [HttpGet("unifiedReturn/{id:long}")]
        public async Task<ActionResult<UnifiedReturnListItemDto>> GetUnifiedReturnById(
            long id,
            [FromQuery] bool includeRaw = true,
            CancellationToken ct = default)
        {
            var row = await _db.UnifiedReturns.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UnifiedReturnId == id, ct);

            if (row is null) return NotFound();
            return Ok(ToReturnListItemDto(row, includeRaw));
        }

        // GET /api/fe/orders/unifiedReturn/by-external/Shopee/250101RETURN
        [HttpGet("unifiedReturn/by-external/{channel}/{externalReturnId}")]
        public async Task<ActionResult<UnifiedReturnListItemDto>> GetUnifiedReturnByExternal(
            string channel,
            string externalReturnId,
            [FromQuery] long? shopId = null,
            [FromQuery] bool includeRaw = true,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(externalReturnId))
                return BadRequest("channel & externalReturnId are required.");

            var qy = _db.UnifiedReturns.AsNoTracking()
                .Where(x => x.Channel == channel && x.ExternalReturnId == externalReturnId);

            if (shopId is > 0)
                qy = qy.Where(x => x.ShopId == shopId.Value);

            var row = await qy
                .OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc ?? x.IngestedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (row is null) return NotFound();
            return Ok(ToReturnListItemDto(row, includeRaw));
        }

        // ====== GET by UnifiedOrderId ======
        // GET /api/fe/orders/20014
        [HttpGet("{id:long}")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> GetById(long id, CancellationToken ct)
        {
            var o = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleOrDefaultAsync(ct);

            if (o is null) return NotFound();
            return Ok(ToListItemDto(o));
        }

        // ====== GET by (Channel + ExternalOrderNo) ======
        // GET /api/fe/orders/by-external/TikTok/251009MP2J3XNQ
        [HttpGet("by-external/{channel}/{externalOrderNo}")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> GetByExternal(
            string channel, string externalOrderNo, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(externalOrderNo))
                return BadRequest("channel & externalOrderNo are required.");

            var o = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.Channel == channel && x.ExternalOrderNo == externalOrderNo)
                .SingleOrDefaultAsync(ct);

            if (o is null) return NotFound();
            return Ok(ToListItemDto(o));
        }

        // POST /api/fe/orders/20014/sync-shopee-escrow
        [HttpPost("{id:long}/sync-shopee-escrow")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> SyncShopeeEscrow(long id, CancellationToken ct)
        {
            var order = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleOrDefaultAsync(ct);

            if (order is null) return NotFound();
            if (!string.Equals(order.Channel, "Shopee", StringComparison.OrdinalIgnoreCase))
                return BadRequest("sync-shopee-escrow supports Shopee orders only.");
            if (order.ShopId is null or <= 0)
                return BadRequest("Shopee shopId is missing.");
            if (string.IsNullOrWhiteSpace(order.ExternalOrderNo))
                return BadRequest("Shopee order number is missing.");

            var json = await _shopee.GetEscrowDetailRawAsync(order.ShopId.Value, order.ExternalOrderNo, ct);
            await _writer.UpsertShopeeEscrowAsync(order.ExternalOrderNo, json, ct);

            var updated = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleAsync(ct);

            return Ok(ToListItemDto(updated));
        }

        // POST /api/fe/orders/20014/sync-tiktok-escrow
        [HttpPost("{id:long}/sync-tiktok-escrow")]
        public async Task<ActionResult<UnifiedOrderListItemDto>> SyncTiktokEscrow(long id, CancellationToken ct)
        {
            var order = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleOrDefaultAsync(ct);

            if (order is null) return NotFound();
            if (!string.Equals(order.Channel, "TikTok", StringComparison.OrdinalIgnoreCase))
                return BadRequest("sync-tiktok-escrow supports TikTok orders only.");
            if (order.ShopId is null or <= 0)
                return BadRequest("TikTok shopId is missing.");
            if (string.IsNullOrWhiteSpace(order.ExternalOrderNo))
                return BadRequest("TikTok order number is missing.");

            var json = await _tiktok.GetOrderEscrowRawAsync(order.ShopId.Value, order.ExternalOrderNo, shopCipher: null, ct);
            await _writer.UpsertTiktokEscrowAsync(order.ExternalOrderNo, json, ct);

            var updated = await _db.VUnifiedOrders.AsNoTracking()
                .Where(x => x.UnifiedOrderId == id)
                .SingleAsync(ct);

            return Ok(ToListItemDto(updated));
        }

        private static IQueryable<UnifiedReturns> ApplyUnifiedReturnFilters(
            IQueryable<UnifiedReturns> qy,
            string? channel,
            long? shopId,
            string? q,
            string? orderRef,
            string? returnRef,
            string? status,
            DateTime? fromUtc,
            DateTime? toUtc,
            string dateField)
        {
            if (!string.IsNullOrWhiteSpace(channel))
                qy = qy.Where(r => r.Channel == channel);

            if (shopId is > 0)
                qy = qy.Where(r => r.ShopId == shopId.Value);

            if (!string.IsNullOrWhiteSpace(orderRef))
                qy = qy.Where(r => r.ExternalOrderId == orderRef);

            if (!string.IsNullOrWhiteSpace(returnRef))
                qy = qy.Where(r => r.ExternalReturnId == returnRef);

            if (!string.IsNullOrWhiteSpace(status))
                qy = qy.Where(r => r.ReturnStatus == status);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim();
                qy = qy.Where(r =>
                    (r.ExternalOrderId ?? "").Contains(kw) ||
                    r.ExternalReturnId.Contains(kw) ||
                    (r.ReturnStatus ?? "").Contains(kw) ||
                    (r.ReturnReason ?? "").Contains(kw) ||
                    (r.TextReason ?? "").Contains(kw) ||
                    r.UnifiedReturnId.ToString().Contains(kw)
                );
            }

            if (fromUtc.HasValue)
            {
                qy = dateField.ToLowerInvariant() switch
                {
                    "created" => qy.Where(r => r.CreatedAtUtc.HasValue && r.CreatedAtUtc.Value >= fromUtc.Value),
                    "ingested" => qy.Where(r => r.IngestedAtUtc >= fromUtc.Value),
                    _ => qy.Where(r => r.UpdatedAtUtc.HasValue && r.UpdatedAtUtc.Value >= fromUtc.Value)
                };
            }

            if (toUtc.HasValue)
            {
                qy = dateField.ToLowerInvariant() switch
                {
                    "created" => qy.Where(r => r.CreatedAtUtc.HasValue && r.CreatedAtUtc.Value <= toUtc.Value),
                    "ingested" => qy.Where(r => r.IngestedAtUtc <= toUtc.Value),
                    _ => qy.Where(r => r.UpdatedAtUtc.HasValue && r.UpdatedAtUtc.Value <= toUtc.Value)
                };
            }

            return qy;
        }

        private static IQueryable<UnifiedReturns> ApplyUnifiedReturnSort(
            IQueryable<UnifiedReturns> qy,
            string? sort)
        {
            return (sort ?? "").ToLowerInvariant() switch
            {
                "createdasc" => qy.OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.UnifiedReturnId),
                "createddesc" => qy.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.UnifiedReturnId),
                "updatedasc" => qy.OrderBy(r => r.UpdatedAtUtc).ThenBy(r => r.UnifiedReturnId),
                "ingestedasc" => qy.OrderBy(r => r.IngestedAtUtc).ThenBy(r => r.UnifiedReturnId),
                "ingesteddesc" => qy.OrderByDescending(r => r.IngestedAtUtc).ThenByDescending(r => r.UnifiedReturnId),
                _ => qy.OrderByDescending(r => r.UpdatedAtUtc ?? r.CreatedAtUtc ?? r.IngestedAtUtc)
                    .ThenByDescending(r => r.UnifiedReturnId),
            };
        }

        private static UnifiedReturnListItemDto ToReturnListItemDto(UnifiedReturns r, bool includeRaw)
        {
            return new UnifiedReturnListItemDto(
                r.UnifiedReturnId,
                r.UnifiedOrderId,
                r.Channel,
                r.ShopId,
                r.ExternalOrderId,
                r.ExternalReturnId,
                r.ReturnStatus,
                r.ReturnReason,
                r.TextReason,
                r.ReturnType,
                r.ReturnSolution,
                r.NegotiationStatus,
                r.RefundAmount,
                r.Currency,
                r.ReturnItemsJson,
                r.ImagesJson,
                r.CreatedAtUtc,
                r.UpdatedAtUtc,
                r.IngestedAtUtc,
                includeRaw ? r.RawJson : null
            );
        }

        private static UnifiedOrderListItemDto ToListItemDto(VUnifiedOrder o)
        {
            var flow = BuildFlowAccountAmounts(o);
            var reconciliation = BuildReconciliationAmounts(o);

            decimal? sellerVoucher = null;
            decimal? shopeeVoucher = null;
            decimal? shippingFeeSstAmount = null;

            if (o.Channel == "Shopee")
            {
                sellerVoucher = GetJsonDecimalPath(o.PayloadEscrowJson, "Shopee", "seller_voucher", o.DiscountSellerAmount, takeAbs: true);
                shopeeVoucher = GetJsonDecimalPath(o.PayloadEscrowJson, "Shopee", "shopee_voucher", o.DiscountPlatformAmount, takeAbs: true);
                shippingFeeSstAmount = GetJsonDecimalPath(o.PayloadEscrowJson, "Shopee", "shipping_fee_sst_amount", o.BuyerPaidShippingFee);
            }
            else if (o.Channel == "TikTok")
            {
                sellerVoucher = o.DiscountSellerAmount;
                shopeeVoucher = o.DiscountPlatformAmount;
                shippingFeeSstAmount = o.BuyerPaidShippingFee ?? o.ShippingFeeAmount;
            }

            return new UnifiedOrderListItemDto(
                o.UnifiedOrderId,
                o.ExternalOrderNo,
                o.Channel,
                o.ShopId,
                o.SellerId,
                o.OrderStatus,
                o.CreatedTimeUtc,
                o.UpdatedTimeUtc,
                o.BuyerUserId,
                o.BuyerUsername,
                o.SubtotalAmount,
                o.DiscountSellerAmount,
                o.DiscountPlatformAmount,
                o.VoucherAmount,
                o.ShippingFeeAmount,
                o.TotalAmount,
                o.PaidAmount,
                o.ItemsJson,
                o.PaymentsJson,
                o.ShipmentsJson,
                o.ShipToJson,
                o.EscrowAmount,
                o.BuyerPaidShippingFee,
                o.ActualShippingFee,
                o.PlatformShippingRebate,
                o.CommissionFee,
                o.ServiceFee,
                o.PlatformFee,
                o.PaymentTransactionFee,
                o.AmsCommissionFee,
                o.SellerVoucherCode,
                flow,
                reconciliation,
                sellerVoucher,
                shopeeVoucher,
                shippingFeeSstAmount
            );
        }

        private static FlowAccountAmountsDto BuildFlowAccountAmounts(VUnifiedOrder o)
        {
            var gross = Money(o.SubtotalAmount);
            var sellerDiscount = Money(o.DiscountSellerAmount);
            var platformDiscount = o.DiscountPlatformAmount.HasValue
                ? Money(o.DiscountPlatformAmount)
                : Money(o.VoucherAmount);
            var shippingFee = Money(o.ShippingFeeAmount);
            var amountDue = Money(o.PaidAmount ?? o.TotalAmount);
            var netAfterSellerDiscount = gross - sellerDiscount;
            var tax = 0m;

            return new FlowAccountAmountsDto(
                GrossAmount: gross,
                SellerDiscountAmount: sellerDiscount,
                NetAfterSellerDiscountAmount: netAfterSellerDiscount,
                PlatformDiscountAmount: platformDiscount,
                AmountDue: amountDue,
                ShippingFeeAmount: shippingFee,
                TaxAmount: tax,
                GrandTotalAmount: amountDue
            );
        }

        private static ReconciliationAmountsDto BuildReconciliationAmounts(VUnifiedOrder o)
        {
            var commissionFee = FeeMoney(o.CommissionFee);
            var serviceFee = FeeMoney(o.ServiceFee);
            var platformFee = FeeMoney(o.PlatformFee);
            var paymentTransactionFee = FeeMoney(o.PaymentTransactionFee);
            var amsCommissionFee = FeeMoney(o.AmsCommissionFee);
            var totalFee = commissionFee + serviceFee + platformFee + paymentTransactionFee + amsCommissionFee;

            return new ReconciliationAmountsDto(
                EscrowAmount: Money(o.EscrowAmount),
                BuyerPaidShippingFee: Money(o.BuyerPaidShippingFee),
                ActualShippingFee: Money(o.ActualShippingFee),
                PlatformShippingRebate: Money(o.PlatformShippingRebate),
                CommissionFee: commissionFee,
                ServiceFee: serviceFee,
                PlatformFee: platformFee,
                PaymentTransactionFee: paymentTransactionFee,
                AmsCommissionFee: amsCommissionFee,
                TotalFeeAmount: totalFee,
                NetPayoutAmount: Money(o.EscrowAmount),
                SellerVoucherCode: o.SellerVoucherCode
            );
        }

        private static decimal? GetJsonDecimalPath(string? json, string channel, string path, decimal? fallback, bool takeAbs = false)
        {
            if (string.IsNullOrWhiteSpace(json)) return fallback;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (channel == "Shopee")
                {
                    if (root.TryGetProperty("response", out var response) && response.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (response.TryGetProperty("buyer_payment_info", out var buyerInfo) && buyerInfo.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (buyerInfo.TryGetProperty(path, out var val))
                            {
                                decimal? result = null;
                                if (val.ValueKind == System.Text.Json.JsonValueKind.Number) result = val.GetDecimal();
                                else if (val.ValueKind == System.Text.Json.JsonValueKind.String && decimal.TryParse(val.GetString(), out var d)) result = d;

                                if (result.HasValue)
                                {
                                    return takeAbs ? Math.Abs(result.Value) : result.Value;
                                }
                            }
                        }
                    }
                }
            }
            catch {}
            return fallback;
        }

        private static decimal Money(decimal? value) => decimal.Round(value ?? 0m, 2);

        private static decimal FeeMoney(decimal? value) => decimal.Round(Math.Abs(value ?? 0m), 2);
    }
}
