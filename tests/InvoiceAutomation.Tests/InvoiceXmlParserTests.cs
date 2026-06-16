using InvoiceAutomation.Services;

namespace InvoiceAutomation.Tests;

public sealed class InvoiceXmlParserTests
{
    [Fact]
    public void Parses_lookup_code_and_seller_fields()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <HDon>
              <DLHDon>
                <NDHDon>
                  <NBan><MST>0123456789</MST><Ten>ACME</Ten></NBan>
                  <TTHDLQuan><KHMSHDon>1</KHMSHDon><KHHDon>C25TAA</KHHDon><SHDon>100</SHDon><NLap>2025-06-15</NLap></TTHDLQuan>
                  <Fkey>ABC-123</Fkey>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        File.WriteAllText(path, xml);
        try
        {
            var parsed = new InvoiceXmlParser().Parse(path);
            Assert.Equal("0123456789", parsed.SellerMst);
            Assert.Equal("ACME", parsed.SellerName);
            Assert.Equal("100", parsed.InvoiceNumber);
            Assert.Equal("ABC-123", parsed.LookupCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parses_TTKhac_Fkey_and_PortalLink()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <HDon>
              <DLHDon>
                <TTChung>
                  <TTKhac>
                    <TTin><TTruong>PortalLink</TTruong><KDLieu>string</KDLieu><DLieu>http://0318381237hd.easyinvoice.com.vn</DLieu></TTin>
                    <TTin><TTruong>Fkey</TTruong><KDLieu>string</KDLieu><DLieu>ASC7NCSV21JtGw</DLieu></TTin>
                  </TTKhac>
                </TTChung>
                <NDHDon>
                  <NBan><MST>0318381237</MST><Ten>A3 CONNECT</Ten></NBan>
                  <SHDon>72</SHDon><NLap>2026-05-21</NLap>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        File.WriteAllText(path, xml);
        try
        {
            var parsed = new InvoiceXmlParser().Parse(path);
            Assert.Equal("ASC7NCSV21JtGw", parsed.LookupCode);
            Assert.Equal("http://0318381237hd.easyinvoice.com.vn", parsed.LookupUrl);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parses_Extra2_from_Petrolimex_style_TTKhac()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <HDon>
              <DLHDon>
                <TTChung>
                  <TTKhac>
                    <TTin><TTruong>Extra1</TTruong><KDLieu>string</KDLieu><DLieu>611015</DLieu></TTin>
                    <TTin><TTruong>Extra2</TTruong><KDLieu>string</KDLieu><DLieu>6110150101151</DLieu></TTin>
                  </TTKhac>
                </TTChung>
                <NDHDon>
                  <NBan><MST>5800000689</MST><Ten>PETROLIMEX</Ten></NBan>
                  <SHDon>253340</SHDon><NLap>2026-05-09</NLap>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        File.WriteAllText(path, xml);
        try
        {
            var parsed = new InvoiceXmlParser().Parse(path);
            Assert.Equal("6110150101151", parsed.LookupCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parses_nested_DCTC_and_MaTC_from_Central_Retail()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <HDon>
              <DLHDon>
                <TTChung>
                  <TTKhac>
                    <TTin><TTruong>ExtHoaDon</TTruong><KDLieu>string</KDLieu>
                      <DLieu><MaCN>9999</MaCN><DCTC>https://hddt.centralretail.com.vn/</DCTC></DLieu>
                    </TTin>
                  </TTKhac>
                </TTChung>
                <NDHDon>
                  <NBan><MST>0105696842</MST><Ten>EB</Ten></NBan>
                  <NMua>
                    <MST>5801473127</MST>
                    <TTKhac>
                      <TTin><TTruong>ExtNMua</TTruong><KDLieu>string</KDLieu>
                        <DLieu><MaTC>426V9KHH6</MaTC><Sbl>CTL-9999-0580</Sbl></DLieu>
                      </TTin>
                    </TTKhac>
                  </NMua>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        File.WriteAllText(path, xml);
        try
        {
            var parsed = new InvoiceXmlParser().Parse(path);
            Assert.Equal("426V9KHH6", parsed.LookupCode);
            Assert.Equal("https://hddt.centralretail.com.vn/", parsed.LookupUrl);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parses_TransactionID_from_MISA_TTKhac()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <HDon>
              <DLHDon>
                <NDHDon>
                  <NBan><MST>5801473127</MST><Ten>VINFARM</Ten></NBan>
                  <SHDon>10</SHDon><NLap>2026-02-27</NLap>
                </NDHDon>
                <TTKhac>
                  <TTin><TTruong>TransactionID</TTruong><KDLieu>string</KDLieu><DLieu>BDF3TJGW78Q4</DLieu></TTin>
                </TTKhac>
              </DLHDon>
            </HDon>
            """;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        File.WriteAllText(path, xml);
        try
        {
            var parsed = new InvoiceXmlParser().Parse(path);
            Assert.Equal("BDF3TJGW78Q4", parsed.LookupCode);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
