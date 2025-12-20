using System.Data;
using System.Text.Json;
using Dapper;
using MDWAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Repos;

public interface IShopeeOrderIngestRepo
{
    Task UpsertOrderWithItemsAsync(long shopId, JsonElement order, CancellationToken ct);
}

public class ShopeeOrderIngestRepo : IShopeeOrderIngestRepo
{
    private readonly AppDbContext _db;
    public ShopeeOrderIngestRepo(AppDbContext db) => _db = db;

    private static DateTime? FromUnix(long? s)
        => s is > 0 ? DateTimeOffset.FromUnixTimeSeconds(s.Value).UtcDateTime : (DateTime?)null;

    public async Task UpsertOrderWithItemsAsync(long shopId, JsonElement order, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        var needClose = conn.State != ConnectionState.Open;
        if (needClose) await conn.OpenAsync(ct);

        try
        {
            // ----- map header -----
            var orderSn = order.GetProperty("order_sn").GetString()!;
            var orderStatus = order.TryGetProperty("order_status", out var os) ? os.GetString() : null;
            var currency = order.TryGetProperty("currency", out var cur) ? cur.GetString() : null;
            var totalAmount = order.TryGetProperty("total_amount", out var ta) ? ta.GetDecimal() : (decimal?)null;
            var paymentMethod = order.TryGetProperty("payment_method", out var pm) ? pm.GetString() : null;
            var cod = order.TryGetProperty("cod", out var cd) ? cd.GetBoolean() : (bool?)null;

            long? buyerUserId = order.TryGetProperty("buyer_user_id", out var bui) ? bui.GetInt64() : (long?)null;
            string? buyerUsername = order.TryGetProperty("buyer_username", out var bun) ? bun.GetString() : null;

            DateTime? createTime = order.TryGetProperty("create_time", out var ct1) ? FromUnix(ct1.GetInt64()) : null;
            DateTime? updateTime = order.TryGetProperty("update_time", out var ut1) ? FromUnix(ut1.GetInt64()) : null;
            DateTime? payTime = order.TryGetProperty("pay_time", out var pt1) && pt1.ValueKind != JsonValueKind.Null ? FromUnix(pt1.GetInt64()) : null;
            DateTime? pickupDoneTime = order.TryGetProperty("pickup_done_time", out var pdt) && pdt.ValueKind != JsonValueKind.Null ? FromUnix(pdt.GetInt64()) : null;
            DateTime? shipByDate = order.TryGetProperty("ship_by_date", out var sbd) ? FromUnix(sbd.GetInt64()) : null;

            // address (mask)
            string? rn = null, rp = null, rt = null, rd = null, rc = null, rs = null, rr = null, rz = null, rf = null;
            if (order.TryGetProperty("recipient_address", out var addr))
            {
                rn = addr.TryGetProperty("name", out var v) ? v.GetString() : null;
                rp = addr.TryGetProperty("phone", out v) ? v.GetString() : null;
                rt = addr.TryGetProperty("town", out v) ? v.GetString() : null;
                rd = addr.TryGetProperty("district", out v) ? v.GetString() : null;
                rc = addr.TryGetProperty("city", out v) ? v.GetString() : null;
                rs = addr.TryGetProperty("state", out v) ? v.GetString() : null;
                rr = addr.TryGetProperty("region", out v) ? v.GetString() : null;
                rz = addr.TryGetProperty("zipcode", out v) ? v.GetString() : null;
                rf = addr.TryGetProperty("full_address", out v) ? v.GetString() : null;
            }

            // upsert header
            const string upsertHeader = @"
MERGE mdw.ShopeeOrders AS t
USING (SELECT @OrderSn AS OrderSn) AS s
ON (t.OrderSn = s.OrderSn)
WHEN MATCHED THEN UPDATE SET
    ShopId=@ShopId, OrderStatus=@OrderStatus, Currency=@Currency, TotalAmount=@TotalAmount,
    PaymentMethod=@PaymentMethod, Cod=@Cod, BuyerUserId=@BuyerUserId, BuyerUsername=@BuyerUsername,
    CreateTime=@CreateTime, UpdateTime=@UpdateTime, PayTime=@PayTime, PickupDoneTime=@PickupDoneTime, ShipByDate=@ShipByDate,
    Recv_Name=@Recv_Name, Recv_Phone=@Recv_Phone, Recv_Town=@Recv_Town, Recv_District=@Recv_District,
    Recv_City=@Recv_City, Recv_State=@Recv_State, Recv_Region=@Recv_Region, Recv_Zipcode=@Recv_Zipcode, Recv_FullAddress=@Recv_FullAddress,
    RawJson=@RawJson, UpdatedAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
(ShopId, OrderSn, OrderStatus, Currency, TotalAmount, PaymentMethod, Cod, BuyerUserId, BuyerUsername,
 CreateTime, UpdateTime, PayTime, PickupDoneTime, ShipByDate,
 Recv_Name, Recv_Phone, Recv_Town, Recv_District, Recv_City, Recv_State, Recv_Region, Recv_Zipcode, Recv_FullAddress,
 RawJson)
VALUES
(@ShopId, @OrderSn, @OrderStatus, @Currency, @TotalAmount, @PaymentMethod, @Cod, @BuyerUserId, @BuyerUsername,
 @CreateTime, @UpdateTime, @PayTime, @PickupDoneTime, @ShipByDate,
 @Recv_Name, @Recv_Phone, @Recv_Town, @Recv_District, @Recv_City, @Recv_State, @Recv_Region, @Recv_Zipcode, @Recv_FullAddress,
 @RawJson);";

            await conn.ExecuteAsync(new CommandDefinition(
                upsertHeader,
                new
                {
                    ShopId = shopId,
                    OrderSn = orderSn,
                    OrderStatus = orderStatus,
                    Currency = currency,
                    TotalAmount = totalAmount,
                    PaymentMethod = paymentMethod,
                    Cod = cod,
                    BuyerUserId = buyerUserId,
                    BuyerUsername = buyerUsername,
                    CreateTime = createTime,
                    UpdateTime = updateTime,
                    PayTime = payTime,
                    PickupDoneTime = pickupDoneTime,
                    ShipByDate = shipByDate,
                    Recv_Name = rn,
                    Recv_Phone = rp,
                    Recv_Town = rt,
                    Recv_District = rd,
                    Recv_City = rc,
                    Recv_State = rs,
                    Recv_Region = rr,
                    Recv_Zipcode = rz,
                    Recv_FullAddress = rf,
                    RawJson = order.GetRawText()
                },
                cancellationToken: ct));

            // ----- items (จาก item_list + package_list) -----
            // เอา mapping package_number/logistics ไปผูกทีละชิ้น
            var packageMap = new Dictionary<long, (string? pkg, string? carrier, string? status, long? chId)>();
            if (order.TryGetProperty("package_list", out var pkgs) && pkgs.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pkgs.EnumerateArray())
                {
                    var pkgNo = p.TryGetProperty("package_number", out var pv) ? pv.GetString() : null;
                    var carrier = p.TryGetProperty("shipping_carrier", out pv) ? pv.GetString() : null;
                    var lgStat = p.TryGetProperty("logistics_status", out pv) ? pv.GetString() : null;
                    long? chId = p.TryGetProperty("logistics_channel_id", out pv) ? pv.GetInt64() : (long?)null;

                    if (p.TryGetProperty("item_list", out var il) && il.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in il.EnumerateArray())
                        {
                            var orderItemId = it.TryGetProperty("order_item_id", out var ov) ? ov.GetInt64() : (long?)null;
                            if (orderItemId is not null)
                                packageMap[orderItemId.Value] = (pkgNo, carrier, lgStat, chId);
                        }
                    }
                }
            }

            if (order.TryGetProperty("item_list", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                const string upsertItem = @"
MERGE mdw.ShopeeOrderItems AS t
USING (SELECT @OrderSn AS OrderSn, @OrderItemId AS OrderItemId) AS s
ON (t.OrderSn = s.OrderSn AND t.OrderItemId = s.OrderItemId)
WHEN MATCHED THEN UPDATE SET
    ItemId=@ItemId, ItemName=@ItemName, ItemSku=@ItemSku, ModelId=@ModelId,
    ModelName=@ModelName, ModelSku=@ModelSku, ModelQty=@ModelQty,
    ModelOriginalPrice=@ModelOriginalPrice, ModelDiscountedPrice=@ModelDiscountedPrice,
    WeightKg=@WeightKg, ImageUrl=@ImageUrl,
    PackageNumber=@PackageNumber, ShippingCarrier=@ShippingCarrier, LogisticsStatus=@LogisticsStatus, LogisticsChannelId=@LogisticsChannelId,
    UpdatedAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT
(OrderSn, OrderItemId, ItemId, ItemName, ItemSku, ModelId, ModelName, ModelSku, ModelQty,
 ModelOriginalPrice, ModelDiscountedPrice, WeightKg, ImageUrl,
 PackageNumber, ShippingCarrier, LogisticsStatus, LogisticsChannelId)
VALUES
(@OrderSn, @OrderItemId, @ItemId, @ItemName, @ItemSku, @ModelId, @ModelName, @ModelSku, @ModelQty,
 @ModelOriginalPrice, @ModelDiscountedPrice, @WeightKg, @ImageUrl,
 @PackageNumber, @ShippingCarrier, @LogisticsStatus, @LogisticsChannelId);";

                foreach (var it in items.EnumerateArray())
                {
                    var orderItemId = it.TryGetProperty("order_item_id", out var v) ? v.GetInt64() : 0;
                    var itemId = it.TryGetProperty("item_id", out v) ? v.GetInt64() : (long?)null;
                    var itemName = it.TryGetProperty("item_name", out v) ? v.GetString() : null;
                    var itemSku = it.TryGetProperty("item_sku", out v) ? v.GetString() : null;

                    var modelId = it.TryGetProperty("model_id", out v) ? v.GetInt64() : (long?)null;
                    var modelName = it.TryGetProperty("model_name", out v) ? v.GetString() : null;
                    var modelSku = it.TryGetProperty("model_sku", out v) ? v.GetString() : null;
                    var qty = it.TryGetProperty("model_quantity_purchased", out v) ? v.GetInt32() : (int?)null;
                    var price0 = it.TryGetProperty("model_original_price", out v) ? v.GetDecimal() : (decimal?)null;
                    var price1 = it.TryGetProperty("model_discounted_price", out v) ? v.GetDecimal() : (decimal?)null;
                    var weight = it.TryGetProperty("weight", out v) ? v.GetDecimal() : (decimal?)null;

                    string? imageUrl = null;
                    if (it.TryGetProperty("image_info", out var img) && img.TryGetProperty("image_url", out var im))
                        imageUrl = im.GetString();

                    packageMap.TryGetValue(orderItemId, out var pkg);

                    await conn.ExecuteAsync(new CommandDefinition(
                        upsertItem,
                        new
                        {
                            OrderSn = orderSn,
                            OrderItemId = orderItemId,
                            ItemId = itemId,
                            ItemName = itemName,
                            ItemSku = itemSku,
                            ModelId = modelId,
                            ModelName = modelName,
                            ModelSku = modelSku,
                            ModelQty = qty,
                            ModelOriginalPrice = price0,
                            ModelDiscountedPrice = price1,
                            WeightKg = weight,
                            ImageUrl = imageUrl,
                            PackageNumber = pkg.pkg,
                            ShippingCarrier = pkg.carrier,
                            LogisticsStatus = pkg.status,
                            LogisticsChannelId = pkg.chId
                        },
                        cancellationToken: ct));
                }
            }
        }
        finally
        {
            if (needClose) await conn.CloseAsync();
        }
    }
}
