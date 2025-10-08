namespace MDWAPI.Helpers;

public static class TiktokApiPaths
{
    //ดึงข้อมูลจัดส่ง
    public const string LogisticsGetShippingInfo = "/api/logistics/shipping_info/query";
   
    //ยืนยันการจัดส่ง
    public const string LogisticsConfirmShip = "/api/logistics/shipping/confirm";
    
    //ขอสร้างเอกสารขนส่ง (label/AWB)
    public const string LogisticsDocumentCreate = "/api/logistics/shipping_document/create";
    
    // ตรวจสอบสถานะเอกสาร
    public const string LogisticsDocumentQuery = "/api/logistics/shipping_document/query";
    
    // ดาวน์โหลดเอกสาร (pdf/zip)
    public const string LogisticsDocumentDownload = "/api/logistics/shipping_document/download";
}
