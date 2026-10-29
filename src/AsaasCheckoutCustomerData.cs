namespace Sufficit.Gateway.Asaas;

/// <summary>Editable payer details; contains no card data.</summary>
public sealed class AsaasCheckoutCustomerData
{
    public string Name { get; set; } = "";
    public string Document { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string AddressNumber { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Province { get; set; } = "";
    public string Complement { get; set; } = "";

    public void Validate()
    {
        var values = new[] { Name, Document, Email, Phone, Address, AddressNumber, PostalCode, Province };
        if (values.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 250 || x.Any(char.IsControl))
            || Complement.Length > 250 || Complement.Any(char.IsControl)
            || !System.Net.Mail.MailAddress.TryCreate(Email, out _)
            || Document.Length is not (11 or 14) || Document.Any(c => !char.IsAsciiDigit(c))
            || Phone.Length is not (10 or 11) || Phone.Any(c => !char.IsAsciiDigit(c))
            || PostalCode.Length != 8 || PostalCode.Any(c => !char.IsAsciiDigit(c)))
            throw new ArgumentException("Confira os dados do pagador e o endereço antes de continuar.");
    }
}
