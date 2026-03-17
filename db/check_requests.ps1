$conn = New-Object System.Data.SqlClient.SqlConnection("Server=localhost;Database=VCINDW;User Id=sa;Password=Admin@9999;TrustServerCertificate=True;")
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT RequestId, PlatformAccountKey, RequestStatus FROM mbw.MemberMappingRequests"
$r = $cmd.ExecuteReader()
while($r.Read()){
    Write-Host "ID=$($r[0]) Key=$($r[1]) Status=$($r[2])"
}
$r.Close()
$conn.Close()
