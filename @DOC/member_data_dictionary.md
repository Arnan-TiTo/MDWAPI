# Data Dictionary: Member System (Schema: `mbw`)

เอกสารฉบับนี้รวบรวมรายละเอียดโครงสร้างฐานข้อมูลของระบบสมาชิก (Member System) ทั้งหมดใน Schema `mbw` โดยอ้างอิงจาก Entity Definitions ในระบบ

---

## 1. Members
ตารางหลักสำหรับจัดเก็บข้อมูลพื้นฐานของสมาชิก

| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **MemberId** | `long` | Primary Key | ไอดีพื้นฐานของสมาชิก |
| **MemberCode** | `string(50)` | Required | รหัสสมาชิก |
| **DisplayName** | `string(200)` | - | ชื่อที่ใช้แสดงผล |
| **Phone** | `string(20)` | - | เบอร์โทรศัพท์ |
| **Email** | `string(200)` | - | อีเมล |
| **MemberType** | `string(50)` | - | ประเภทสมาชิก |
| **FirstName** | `string(100)` | - | ชื่อจริง |
| **LastName** | `string(100)` | - | นามสกุล |
| **BirthDate** | `DateTime?` | - | วันเกิด |
| **Age** | `int?` | - | อายุ |
| **Gender** | `string(20)` | - | เพศ |
| **Address** | `string(500)` | - | ที่อยู่ |
| **Subdistrict** | `string(100)` | - | ตำบล/แขวง |
| **District** | `string(100)` | - | อำเภอ/เขต |
| **Province** | `string(100)` | - | จังหวัด |
| **ZipCode** | `string(20)` | - | รหัสไปรษณีย์ |
| **MembershipTier**| `string(100)` | - | ระดับสมาชิก (Tier) |
| **Tags** | `string(1000)`| - | แท็กสำหรับแบ่งกลุ่มสมาชิก |
| **Branch** | `string(100)` | - | สาขาที่สมัครหรือใช้บริการหลัก |
| **PointsForTier**| `decimal` | - | คะแนนสะสมที่ใช้สำหรับคำวณ Tier |
| **UsageCount** | `int` | - | จำนวนครั้งที่เข้าใช้งาน |
| **LastActiveAt** | `DateTime?` | - | ใช้งานล่าสุดเมื่อ |
| **LastActiveDays**| `int?` | - | จำนวนวันที่ไม่ได้ใช้งาน (Dormant days) |
| **Status** | `string(20)` | Default: "Active"| สถานะสมาชิก (Active, Inactive, etc.) |
| **ConsentAccepted**| `bool` | - | ยอมรับ Consent แล้วหรือไม่ |
| **ConsentedAt** | `DateTime?` | - | เวลาที่ยอมรับ Consent |
| **RegisteredAt** | `DateTime` | - | วันที่ลงทะเบียนสมาชิก |
| **CompanysId** | `int?` | FK -> dbo.Companys| ไอดีบริษัทที่สังกัด |
| **HowYouKnowMe** | `string(1000)`| - | ช่องที่รู้จักเรา |
| **CreatedAt** | `DateTime` | - | เวลาที่สร้าง Record |
| **UpdatedAt** | `DateTime?` | - | เวลาที่แก้ไขล่าสุด |

---

## 2. MemberIdentities
ตารางสำหรับเชื่อมต่อ Identity จากภายนอก (เช่น LINE, Facebook, Google)

| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **MemberIdentityId**| `long` | Primary Key | - |
| **MemberId** | `long` | FK -> Members | ไอดีสมาชิก |
| **ProviderType** | `string(30)` | Required | ประเภท Provider (เช่น LINE) |
| **ProviderUserKey**| `string(200)` | Required | Key อ้างอิงจาก Provider (เช่น UserID) |
| **DisplayName** | `string(200)` | - | ชื่อจาก Provider |
| **PictureUrl** | `string(500)` | - | URL รูปภาพจาก Provider |
| **LinkedAt** | `DateTime` | - | เวลาที่เชื่อมต่อ |
| **IsActive** | `bool` | Default: true | สถานะการใช้งาน |

---

## 3. MemberPlatformAccounts
ตารางสำหรับเก็บข้อมูลบัญชีลูกค้าใน Platform ต่างๆ (Shopee, Lazada, TikTok) เพื่อใช้ในการเชื่อมโยงออเดอร์

| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **MemberPlatformAccountId**| `long` | Primary Key | - |
| **MemberId** | `long` | FK -> Members | ไอดีสมาชิก |
| **PlatformType** | `string(20)` | Required | ประเภทแพลตฟอร์ม (Shopee, Lazada) |
| **ShopId** | `int?` | FK -> mdw.Shops | ไอดีร้านค้า |
| **PlatformAccountKey**| `string(200)` | Required | Key ของบัญชี (เช่น Username/BuyerID) |
| **PlatformAccountName**| `string(200)` | - | ชื่อบัญชีในแพลตฟอร์ม |
| **VerifiedStatus**| `string(20)` | Default: "Pending"| สถานะการตรวจสอบ (Pending, Verified) |
| **VerifiedAt** | `DateTime?` | - | เวลาที่ตรวจสอบสำเร็จ |
| **VerifiedBy** | `string(100)` | - | ผู้ดำเนินการตรวจสอบ |
| **LinkMethod** | `string(20)` | Default: "MANUAL"| วิธีการเชื่อมต่อ (MANUAL, AUTO) |
| **ConfidenceScore**| `decimal?` | - | คะแนนความมั่นใจในการ Matching |
| **IsPrimary** | `bool` | - | เป็นบัญชีหลักของแพลตฟอร์มนั้นหรือไม่ |

---

## 4. MemberMappingRequests & Evidence
ตารางสำหรับเก็บคำร้องขอเชื่อมต่อบัญชีสมาชิกกับแพลตฟอร์มขายของ

### MemberMappingRequests
| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **RequestId** | `long` | Primary Key | - |
| **MemberId** | `long` | FK -> Members | - |
| **PlatformType** | `string(20)` | - | - |
| **ShopId** | `int?` | - | - |
| **PlatformAccountKey**| `string(200)` | - | - |
| **RequestStatus** | `string(20)` | - | - |
| **SourceType** | `string(30)` | - | ADMIN, CUSTOMER |

### MemberMappingEvidence
| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **EvidenceId** | `long` | Primary Key | - |
| **RequestId** | `long` | FK -> Requests | - |
| **EvidenceType** | `string(30)` | - | ประเภทหลักฐาน (PHONE, ORDER_ID, etc.) |
| **EvidenceValue**| `string` | - | ค่าของหลักฐาน (เช่น "0812345678") |

---

## 5. OrderMemberLinks & Claims
ตารางเชื่อมโยงออเดอร์จากระบบขายกับสมาชิกในระบบ Membership

### OrderMemberLinks
| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **OrderMemberLinkId**| `long` | Primary Key | - |
| **UnifiedOrderId**| `long` | FK -> mdw.Orders | ไอดีออเดอร์รวม |
| **MemberId** | `long` | FK -> Members | ไอดีสมาชิก |
| **LinkMethod** | `string(30)` | Required | วิธีเชื่อม (VERIFIED_ACCOUNT / CLAIM) |
| **LinkedAt** | `DateTime` | - | - |

---

## 6. Point Management (Points)
ระบบจัดการคะแนนสะสม

### PointAccounts
| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **AvailablePoints**| `int` | - | คะแนนที่ใช้งานได้ปัจจุบัน |
| **PendingPoints** | `int` | - | คะแนนที่รอการตรวจสอบ (เช่น ออเดอร์ยังไม่จบ) |
| **ReservedPoints** | `int` | - | คะแนนที่จองไว้สำหรับการแลกของ |
| **TotalEarned** | `int` | - | คะแนนสะสมทั้งหมดที่เคยได้รับ |
| **TotalBurned** | `int` | - | คะแนนทั้งหมดที่เคยแลกไป |

### PointLedger
| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **TxnType** | `string(20)` | EARN, BURN, etc. | ประเภทรายการคะแนน |
| **Points** | `int` | - | จำนวนคะแนนในรายการนี้ |
| **BalanceAfter** | `int` | - | คะแนนคงเหลือหลังจบรายการนี้ |
| **RefType** | `string(30)` | ORDER, REDEMPTION| อ้างอิงถึงโมดูลอื่น |
| **RefId** | `string(100)` | - | เลขอ้างอิง |

---

## 7. Reward & Redemption
ระบบแลกของรางวัล

### RewardCatalog
| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **RewardName** | `string(200)` | - | ชื่อของรางวัล |
| **RewardType** | `string(30)` | - | DISCOUNT_CODE, etc. |
| **PointsCost** | `int` | - | คะแนนที่ต้องใช้แลก |
| **StockRemaining**| `int` | - | จำนวนของที่เหลือ |

### RewardRedemptions
| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **MemberId** | `long` | - | สมาชิกผู้แลก |
| **RewardId** | `int` | - | ของรางวัลที่แลก |
| **Status** | `string(20)` | Reserved, Completed| สถานะการแลก |
| **PointsSpent** | `int` | - | คะแนนที่ใช้จริง |

---

## 8. OutboxMessages
ตารางส่งข้อความหาลูกแจ้งเตือนผ่านช่องทางต่างๆ (LINE)

| Field | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| **MessageType** | `string(30)` | - | ประเภทข้อความ (POINT_EARN, etc.) |
| **Channel** | `string(20)` | Default: "LINE" | |
| **Payload** | `string` | - | เนื้อหาข้อความหรือ JSON Data |
| **Status** | `string(20)` | Pending, Sent, Fail| |
