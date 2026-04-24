-- สร้างตาราง mdw.UnifiedOrderLabelDocument สำหรับเก็บข้อมูล shipping document data info จาก Shopee
IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'mdw' AND t.name = 'UnifiedOrderLabelDocument'
)
BEGIN
    CREATE TABLE mdw.UnifiedOrderLabelDocument (
        Id                          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Platform                    NVARCHAR(20)    NOT NULL,
        ShopId                      BIGINT          NOT NULL,
        OrderSn                     NVARCHAR(100)   NOT NULL,
        PackageNumber               NVARCHAR(100)   NULL,

        -- Status
        Status                      NVARCHAR(50)    NULL,   -- READY, FAILED, etc.
        FailError                   NVARCHAR(200)   NULL,
        FailMessage                 NVARCHAR(500)   NULL,
        DocumentType                NVARCHAR(50)    NULL,   -- NORMAL_AIR_WAYBILL, etc.

        -- Logistics
        TrackingNumber              NVARCHAR(100)   NULL,
        LogisticsChannelName        NVARCHAR(200)   NULL,
        CodAmount                   DECIMAL(18,2)   NULL,
        ProductCount                INT             NULL,
        PurchaseOrderNumber         NVARCHAR(100)   NULL,
        PurchaseOrderPrefix         NVARCHAR(50)    NULL,
        BuyerRemark                 NVARCHAR(500)   NULL,
        ParcelChargeableWeightGram  INT             NULL,
        BillingWeight               DECIMAL(18,4)   NULL,

        -- Sender
        SenderName                  NVARCHAR(200)   NULL,
        SenderPhone                 NVARCHAR(50)    NULL,
        SenderFullAddress           NVARCHAR(500)   NULL,
        SenderZipcode               NVARCHAR(20)    NULL,

        -- Recipient
        RecipientName               NVARCHAR(200)   NULL,
        RecipientPhone              NVARCHAR(50)    NULL,
        RecipientTown               NVARCHAR(100)   NULL,
        RecipientDistrict           NVARCHAR(100)   NULL,
        RecipientCity               NVARCHAR(100)   NULL,
        RecipientState              NVARCHAR(100)   NULL,
        RecipientRegion             NVARCHAR(100)   NULL,
        RecipientZipcode            NVARCHAR(20)    NULL,
        RecipientFullAddress        NVARCHAR(500)   NULL,

        -- Sort codes
        AsSortCode                  NVARCHAR(100)   NULL,
        CpSortCode                  NVARCHAR(100)   NULL,
        FmSortCode                  NVARCHAR(100)   NULL,

        -- QR Code plain text (third_party_logistic_info.qrcode)
        QrCodeData                  NVARCHAR(MAX)   NULL,

        -- JSON blobs
        ItemListJson                NVARCHAR(MAX)   NULL,
        RawJson                     NVARCHAR(MAX)   NULL,

        -- recipient_address_info: raw array (รวม base64 images) + decoded map {key: text}
        RecipientAddrRawJson        NVARCHAR(MAX)   NULL,
        RecipientAddrDecodedJson    NVARCHAR(MAX)   NULL,

        -- sender_address_info (ถ้ามี): raw + decoded
        SenderAddrRawJson           NVARCHAR(MAX)   NULL,
        SenderAddrDecodedJson       NVARCHAR(MAX)   NULL,

        -- Audit
        FetchedAt                   DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt                   DATETIME2       NULL
    );

    CREATE UNIQUE INDEX UX_UnifiedOrderLabelDocument_Key
        ON mdw.UnifiedOrderLabelDocument (Platform, ShopId, OrderSn, PackageNumber)
        WHERE PackageNumber IS NOT NULL;

    CREATE INDEX IX_UnifiedOrderLabelDocument_OrderSn
        ON mdw.UnifiedOrderLabelDocument (OrderSn);

    PRINT 'Created mdw.UnifiedOrderLabelDocument';
END
ELSE
    PRINT 'mdw.UnifiedOrderLabelDocument already exists';
GO
