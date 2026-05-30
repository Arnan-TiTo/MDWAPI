CREATE OR ALTER PROCEDURE mdw.[USP_Normalize_TikTok_Order]
    @ShopId            BIGINT,
    @SellerId          NVARCHAR(100) = NULL,
    @BatchNo           NVARCHAR(40)  = NULL,
    @Env               NVARCHAR(50)  = NULL,
    @RawOrder          NVARCHAR(MAX),

    @UnifiedOrderId    BIGINT        OUTPUT,
    @ExternalOrderId   NVARCHAR(100) OUTPUT,
    @Outcome           NVARCHAR(20)  OUTPUT,   -- Created | Updated | Unchanged
    @RawHash512        VARBINARY(64) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Channel NVARCHAR(20) = N'TikTok';
    DECLARE @nowUtc DATETIME2(3) = SYSUTCDATETIME();

    /* 0) hash */
    DECLARE @payloadHash VARBINARY(32) = HASHBYTES('SHA2_256', CONVERT(VARBINARY(MAX), @RawOrder));
    SET @RawHash512 = HASHBYTES('SHA2_512', CONVERT(VARBINARY(MAX), @RawOrder));

    /* 1) external id */
    SELECT @ExternalOrderId = COALESCE(
        JSON_VALUE(@RawOrder, '$.id'),
        JSON_VALUE(@RawOrder, '$.order_id'),
        JSON_VALUE(@RawOrder, '$.orderId'),
        JSON_VALUE(@RawOrder, '$.order_number'),
        JSON_VALUE(@RawOrder, '$.orderNumber')
    );
    IF @ExternalOrderId IS NULL
        THROW 51000, 'TikTok: external order id not found in payload.', 1;

    /* 2) persist raw */
    DECLARE @RawIdTable TABLE (RawId BIGINT);
    MERGE mdw.UnifiedRawOrders AS T
    USING (SELECT @Channel AS Channel, @ShopId AS ShopId, @SellerId AS SellerId,
                  @ExternalOrderId AS ExternalOrderId, @payloadHash AS PayloadHash) S
       ON (T.Channel = S.Channel AND T.ExternalOrderId = S.ExternalOrderId AND T.PayloadHash = S.PayloadHash)
    WHEN NOT MATCHED THEN
        INSERT (Channel, ShopId, SellerId, ExternalOrderId, PayloadJson, BatchNo, PayloadHash)
        VALUES (@Channel, @ShopId, @SellerId, @ExternalOrderId, @RawOrder, @BatchNo, @payloadHash)
    OUTPUT inserted.RawId INTO @RawIdTable;

    DECLARE @RawId BIGINT = (SELECT TOP(1) RawId FROM @RawIdTable);
    IF @RawId IS NULL
        SELECT @RawId = RawId
        FROM mdw.UnifiedRawOrders
        WHERE Channel=@Channel AND ExternalOrderId=@ExternalOrderId AND PayloadHash=@payloadHash;

    /* 3) head fields */
    DECLARE
        @status NVARCHAR(40) = JSON_VALUE(@RawOrder, '$.status'),
        @fulfill NVARCHAR(40) = JSON_VALUE(@RawOrder, '$.line_items[0].package_status'),
        @isCod BIT = TRY_CAST(JSON_VALUE(@RawOrder, '$.is_cod') AS BIT),

        @currency NVARCHAR(8) = JSON_VALUE(@RawOrder, '$.payment.currency'),
        @subtotal DECIMAL(18,2) = TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(@RawOrder, '$.payment.sub_total')),
        @discSeller DECIMAL(18,2) = TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(@RawOrder, '$.payment.seller_discount')),
        @discPlatform DECIMAL(18,2) = TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(@RawOrder, '$.payment.platform_discount')),
        @voucher DECIMAL(18,2) = NULL,
        @shipFee DECIMAL(18,2) = TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(@RawOrder, '$.payment.shipping_fee')),
        @taxAmt DECIMAL(18,2) = TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(@RawOrder, '$.payment.tax')),
        @otherFee DECIMAL(18,2) = NULL,
        @totalAmt DECIMAL(18,2) = TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(@RawOrder, '$.payment.total_amount')),
        @paidAmt DECIMAL(18,2) = NULL,

        @paymentMethod NVARCHAR(60) = COALESCE(JSON_VALUE(@RawOrder, '$.payment_method_name'),
                                               JSON_VALUE(@RawOrder, '$.payment_method_code')),

        @shipProvider NVARCHAR(120) = COALESCE(JSON_VALUE(@RawOrder, '$.shipping_provider'),
                                               JSON_VALUE(@RawOrder, '$.line_items[0].shipping_provider_name')),
        @shipSvc NVARCHAR(80) = COALESCE(JSON_VALUE(@RawOrder, '$.shipping_type'),
                                         JSON_VALUE(@RawOrder, '$.delivery_option_name')),
        @tracking NVARCHAR(120) = COALESCE(JSON_VALUE(@RawOrder, '$.tracking_number'),
                                           JSON_VALUE(@RawOrder, '$.line_items[0].tracking_number')),
        @warehouse NVARCHAR(80) = JSON_VALUE(@RawOrder, '$.warehouse_id'),

        @buyerUser NVARCHAR(120) = JSON_VALUE(@RawOrder, '$.user_id'),
        @buyerName NVARCHAR(200) = COALESCE(JSON_VALUE(@RawOrder, '$.recipient_address.name'), JSON_VALUE(@RawOrder, '$.buyer_name')),
        @buyerPhone NVARCHAR(60) = JSON_VALUE(@RawOrder, '$.recipient_address.phone_number'),
        @buyerEmail NVARCHAR(200) = JSON_VALUE(@RawOrder, '$.buyer_email'),

        @noteBuyer NVARCHAR(1000) = JSON_VALUE(@RawOrder, '$.buyer_message'),
        @noteSeller NVARCHAR(1000) = NULL;

    -- epoch s/ms → datetime2(3)
    DECLARE
        @create_epoch BIGINT = TRY_CONVERT(BIGINT, JSON_VALUE(@RawOrder, '$.create_time')),
        @update_epoch BIGINT = TRY_CONVERT(BIGINT, JSON_VALUE(@RawOrder, '$.update_time')),
        @paid_epoch   BIGINT = TRY_CONVERT(BIGINT, JSON_VALUE(@RawOrder, '$.paid_time')),
        @cancel_epoch BIGINT = TRY_CONVERT(BIGINT, JSON_VALUE(@RawOrder, '$.cancel_time'));

    DECLARE
        @createUtc DATETIME2(3) = CASE WHEN @create_epoch IS NULL THEN NULL ELSE DATEADD(SECOND, CASE WHEN @create_epoch >= 1000000000000 THEN @create_epoch/1000 ELSE @create_epoch END, '1970-01-01') END,
        @updateUtc DATETIME2(3) = CASE WHEN @update_epoch IS NULL THEN NULL ELSE DATEADD(SECOND, CASE WHEN @update_epoch >= 1000000000000 THEN @update_epoch/1000 ELSE @update_epoch END, '1970-01-01') END,
        @paidUtc   DATETIME2(3) = CASE WHEN @paid_epoch   IS NULL THEN NULL ELSE DATEADD(SECOND, CASE WHEN @paid_epoch   >= 1000000000000 THEN @paid_epoch/1000   ELSE @paid_epoch   END, '1970-01-01') END,
        @cancelUtc DATETIME2(3) = CASE WHEN @cancel_epoch IS NULL THEN NULL ELSE DATEADD(SECOND, CASE WHEN @cancel_epoch >= 1000000000000 THEN @cancel_epoch/1000 ELSE @cancel_epoch END, '1970-01-01') END,
        @shipUtc   DATETIME2(3) = NULL,
        @delivUtc  DATETIME2(3) = NULL,
        @doneUtc   DATETIME2(3) = NULL;

    IF @paidUtc IS NOT NULL SET @paidAmt = @totalAmt;

    /* 4) ADDRESS from recipient_address + district_info (L0/L1/L2) */
    DECLARE
        @addrType NVARCHAR(20) = COALESCE(JSON_VALUE(@RawOrder, '$.delivery_type'), N'ShipTo'), -- ex: HOME_DELIVERY
        @addrName NVARCHAR(200) = JSON_VALUE(@RawOrder, '$.recipient_address.name'),
        @addrPhone NVARCHAR(60) = JSON_VALUE(@RawOrder, '$.recipient_address.phone_number'),
        @addrEmail NVARCHAR(200) = @buyerEmail,
        @postal NVARCHAR(20) = JSON_VALUE(@RawOrder, '$.recipient_address.postal_code'),
        @line1 NVARCHAR(300) = JSON_VALUE(@RawOrder, '$.recipient_address.address_line1'),
        @line2 NVARCHAR(300) = JSON_VALUE(@RawOrder, '$.recipient_address.address_line2'),
        @full  NVARCHAR(1000)= JSON_VALUE(@RawOrder, '$.recipient_address.full_address'),
        @country NVARCHAR(80),
        @state   NVARCHAR(120),
        @city    NVARCHAR(120) = NULL,
        @district NVARCHAR(120);

    ;WITH di AS (
        SELECT
            JSON_VALUE(x.value,'$.address_level') AS lvl,
            JSON_VALUE(x.value,'$.address_name')  AS name
        FROM OPENJSON(JSON_QUERY(@RawOrder, '$.recipient_address.district_info')) x
    )
    SELECT
        @country  = MAX(CASE WHEN lvl='L0' THEN name END),
        @state    = MAX(CASE WHEN lvl='L1' THEN name END),
        @district = MAX(CASE WHEN lvl='L2' THEN name END)
    FROM di;

    DECLARE @ShipToId BIGINT;

    -- de-dup (ค่าเท่ากันทุกช่อง)
    SELECT TOP (1) @ShipToId = UnifiedOrderAddressId
    FROM mdw.UnifiedOrderAddresses
    WHERE [Type] = @addrType
      AND ISNULL(Name,'')       = ISNULL(@addrName,'')
      AND ISNULL(Phone,'')      = ISNULL(@addrPhone,'')
      AND ISNULL(Email,'')      = ISNULL(@addrEmail,'')
      AND ISNULL(Country,'')    = ISNULL(@country,'')
      AND ISNULL([State],'')    = ISNULL(@state,'')
      AND ISNULL(City,'')       = ISNULL(@city,'')
      AND ISNULL(District,'')   = ISNULL(@district,'')
      AND ISNULL(PostalCode,'') = ISNULL(@postal,'')
      AND ISNULL(Address1,'')   = ISNULL(@line1,'')
      AND ISNULL(Address2,'')   = ISNULL(@line2,'')
      AND ISNULL(FullAddress,'')= ISNULL(@full,'');

    IF @ShipToId IS NULL
    BEGIN
        INSERT INTO mdw.UnifiedOrderAddresses
            ([Type], Name, Phone, Email, Country, [State], City, District, PostalCode, Address1, Address2, FullAddress, Latitude, Longitude)
        VALUES
            (@addrType, @addrName, @addrPhone, @addrEmail, @country, @state, @city, @district, @postal, @line1, @line2, @full, NULL, NULL);
        SET @ShipToId = SCOPE_IDENTITY();
    END

    /* 5) Upsert UnifiedOrders */
    DECLARE @ExistingId BIGINT, @ExistingHash VARBINARY(32);
    SELECT @ExistingId = UnifiedOrderId, @ExistingHash = SourcePayloadHash
      FROM mdw.UnifiedOrders
     WHERE Channel=@Channel AND ExternalOrderId=@ExternalOrderId;

    IF @ExistingId IS NULL
    BEGIN
        INSERT INTO mdw.UnifiedOrders
        (
            Channel, ShopId, SellerId,
            ExternalOrderId, ExternalOrderNo,
            OrderStatus, FulfillmentStatus, PaymentStatus,
            Currency, SubtotalAmount, DiscountSellerAmount, DiscountPlatformAmount, VoucherAmount,
            ShippingFeeAmount, TaxAmount, OtherFeeAmount,
            TotalAmount, PaidAmount, RefundAmount,
            PaymentMethod, ShipmentProvider, ShipmentServiceCode, TrackingNo, WarehouseCode,
            BuyerUserId, BuyerName, BuyerPhone, BuyerEmail,
            ShipToAddressId, BillToAddressId,
            CreatedTimeUtc, UpdatedTimeUtc, PaidTimeUtc, CancelTimeUtc, ShippedTimeUtc, DeliveredTimeUtc, CompletedTimeUtc,
            NoteBuyer, NoteSeller,
            SourceRawId, SourcePayloadHash, IngestBatchNo, IngestedAtUtc
        )
        VALUES
        (
            @Channel, @ShopId, @SellerId,
            @ExternalOrderId, NULL,
            @status, @fulfill, CASE WHEN @paidUtc IS NULL THEN NULL ELSE N'PAID' END,
            @currency, @subtotal, @discSeller, @discPlatform, @voucher,
            @shipFee, @taxAmt, @otherFee,
            @totalAmt, @paidAmt, NULL,
            @paymentMethod, @shipProvider, @shipSvc, @tracking, @warehouse,
            @buyerUser, @buyerName, @buyerPhone, @buyerEmail,
            @ShipToId, NULL,
            @createUtc, @updateUtc, @paidUtc, @cancelUtc, NULL, NULL, NULL,
            @noteBuyer, @noteSeller,
            @RawId, @payloadHash, @BatchNo, @nowUtc
        );

        SET @UnifiedOrderId = SCOPE_IDENTITY();
        SET @Outcome = N'Created';
    END
    ELSE
    BEGIN
        SET @UnifiedOrderId = @ExistingId;

        IF @ExistingHash = @payloadHash
        BEGIN
            SET @Outcome = N'Unchanged';
        END
        ELSE
        BEGIN
            UPDATE mdw.UnifiedOrders
            SET
                ShopId=@ShopId, SellerId=@SellerId,
                OrderStatus=@status, FulfillmentStatus=@fulfill,
                PaymentStatus=CASE WHEN @paidUtc IS NULL THEN NULL ELSE N'PAID' END,
                Currency=@currency,
                SubtotalAmount=@subtotal, 
                DiscountSellerAmount=@discSeller,
                DiscountPlatformAmount=@discPlatform, 
                VoucherAmount=@voucher,
                ShippingFeeAmount=@shipFee, TaxAmount=@taxAmt, OtherFeeAmount=@otherFee,
                TotalAmount=@totalAmt, PaidAmount=@paidAmt,
                PaymentMethod=@paymentMethod, ShipmentProvider=@shipProvider, ShipmentServiceCode=@shipSvc, TrackingNo=@tracking, WarehouseCode=@warehouse,
                BuyerUserId=@buyerUser, BuyerName=@buyerName, BuyerPhone=@buyerPhone, BuyerEmail=@buyerEmail,
                ShipToAddressId=@ShipToId, BillToAddressId=NULL,
                CreatedTimeUtc=@createUtc, UpdatedTimeUtc=@updateUtc, PaidTimeUtc=@paidUtc, CancelTimeUtc=@cancelUtc,
                ShippedTimeUtc=NULL, DeliveredTimeUtc=NULL, CompletedTimeUtc=NULL,
                NoteBuyer=@noteBuyer, NoteSeller=@noteSeller,
                SourceRawId=@RawId, SourcePayloadHash=@payloadHash, IngestBatchNo=@BatchNo, IngestedAtUtc=@nowUtc
            WHERE UnifiedOrderId=@UnifiedOrderId;

            SET @Outcome = N'Updated';
        END
    END

    /* 6) Children */
    DELETE FROM mdw.UnifiedOrderPayments WHERE UnifiedOrderId=@UnifiedOrderId;
    INSERT INTO mdw.UnifiedOrderPayments
        (UnifiedOrderId, [Method], ChannelTxnId, PaidAmount, Currency, PaidTimeUtc, FeeAmount, FeeDetailsJson, IsCOD)
    VALUES
        (@UnifiedOrderId, @paymentMethod, NULL, @paidAmt, @currency, @paidUtc, NULL, NULL, @isCod);

    DELETE FROM mdw.UnifiedOrderShipments WHERE UnifiedOrderId=@UnifiedOrderId;
    INSERT INTO mdw.UnifiedOrderShipments
        (UnifiedOrderId, Provider, ServiceCode, TrackingNo, Status, PickupTimeUtc, ShippedTimeUtc, DeliveredTimeUtc, FirstMileCarrier, LastMileCarrier, RawJson)
    VALUES
        (@UnifiedOrderId, @shipProvider, @shipSvc, @tracking, @fulfill, NULL, NULL, NULL, NULL, NULL,
         JSON_QUERY(@RawOrder, '$.packages'));

    DELETE FROM mdw.UnifiedOrderItems WHERE UnifiedOrderId=@UnifiedOrderId;
    INSERT INTO mdw.UnifiedOrderItems
    (
        UnifiedOrderId, ExternalItemId, ProductName, VariationName, SellerSku, PlatformSku,
        QtyOrdered, QtyCanceled, QtyShipped,
        UnitPrice, OriginalPrice, DiscountSeller, DiscountPlatform, TaxAmount, ShippingAlloc, LineTotal, AttributesJson
    )
    SELECT
        @UnifiedOrderId,
        JSON_VALUE(li.value, '$.id'),
        JSON_VALUE(li.value, '$.product_name'),
        JSON_VALUE(li.value, '$.sku_name'),
        JSON_VALUE(li.value, '$.seller_sku'),
        JSON_VALUE(li.value, '$.sku_id'),
        1,
        CASE WHEN UPPER(JSON_VALUE(li.value, '$.display_status'))='CANCELLED' THEN 1 ELSE 0 END,
        0,
        TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(li.value, '$.sale_price')),
        TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(li.value, '$.original_price')),
        TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(li.value, '$.seller_discount')),
        TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(li.value, '$.platform_discount')),
        NULL, NULL,
        TRY_CONVERT(DECIMAL(18,2), JSON_VALUE(li.value, '$.sale_price')),
        li.value
    FROM OPENJSON(@RawOrder, '$.line_items') li;
END;