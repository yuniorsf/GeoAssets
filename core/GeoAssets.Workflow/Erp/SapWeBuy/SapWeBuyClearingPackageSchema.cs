namespace GeoAssets.Workflow.Erp.SapWeBuy;

/// <summary>
/// The XSD <see cref="SapWeBuyClearingPackageExporter"/> validates its own output against.
///
/// SAP's actual WeBUY clearing-package schema is a confidential artifact of Enel/Neoenergia's
/// own SAP tenant and isn't available anywhere in this repository or environment — the same gap
/// <c>BdgdFileGeodatabaseExporter</c> (XD01-225) hit with ANEEL's real FileGDB structure. This is
/// GeoAssets' own authored schema, capturing exactly the required reference fields XD01-255's AC
/// names (PO number, outline agreement number, itemized BoQ, evidence deep links) so the exporter
/// has a concrete, testable contract to validate against rather than an unverifiable claim of
/// "schema-conformant."
/// </summary>
internal static class SapWeBuyClearingPackageSchema
{
    public const string Xsd = """
        <?xml version="1.0" encoding="UTF-8"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="WeBuyClearingPackage">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="Items">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="Item" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:sequence>
                            <xs:element name="EvidenceDeepLinks" minOccurs="0">
                              <xs:complexType>
                                <xs:sequence>
                                  <xs:element name="DeepLink" type="xs:anyURI" maxOccurs="unbounded"/>
                                </xs:sequence>
                              </xs:complexType>
                            </xs:element>
                          </xs:sequence>
                          <xs:attribute name="itemCode" type="xs:string" use="required"/>
                          <xs:attribute name="quantity" type="xs:decimal" use="required"/>
                          <xs:attribute name="unitUs" type="xs:decimal" use="required"/>
                          <xs:attribute name="totalUs" type="xs:decimal" use="required"/>
                          <xs:attribute name="totalBrl" type="xs:decimal" use="required"/>
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
              <xs:attribute name="poNumber" type="xs:string" use="required"/>
              <xs:attribute name="outlineAgreementNumber" type="xs:string" use="required"/>
              <xs:attribute name="serviceOrderId" type="xs:string" use="required"/>
              <xs:attribute name="contractorOrganizationId" type="xs:string" use="required"/>
              <xs:attribute name="contractorName" type="xs:string" use="required"/>
              <xs:attribute name="totalBrl" type="xs:decimal" use="required"/>
              <xs:attribute name="retentionBrl" type="xs:decimal" use="required"/>
              <xs:attribute name="netPayableBrl" type="xs:decimal" use="required"/>
              <xs:attribute name="dossierSignature" type="xs:string" use="required"/>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;
}
