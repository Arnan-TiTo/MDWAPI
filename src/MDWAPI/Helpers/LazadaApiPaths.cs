namespace MDWAPI.Helpers;

public static class LazadaApiPaths
{
    // ดึงสถานะ/เลขติดตามพัสดุ
    public const string LogisticsGetTracking = "/rest/logistics/tracking/get";
    
    // ยืนยันการจัดส่ง/นัดรับ (ขึ้นกับพารามิเตอร์ของวิธีขนส่ง)
    public const string LogisticsShipOrder = "/rest/logistics/ship";

    // ขอสร้าง/ดึงเอกสารจัดส่ง (เช่น Label/Invoice)
    public const string LogisticsGetShipmentDoc = "/rest/logistics/document/get";

    // ดาวน์โหลด Waybill/Label
    public const string LogisticsPrintWaybill = "/rest/logistics/waybill/print";
}
