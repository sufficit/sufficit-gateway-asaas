using Sufficit.Gateway;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Controlled, provider-wide Asaas diagnostics. Only fixed operations are
/// executable; arbitrary URLs, methods and headers are intentionally rejected.
/// </summary>
public sealed partial class AsaasGateway : IGatewayDiagnosticsGateway
{
    private static readonly IReadOnlyList<GatewayDiagnosticOperation> Operations =
        new GatewayDiagnosticOperation[]
        {
            Operation("authentication", "Conta", "Validar autenticação", "Consulta os dados comerciais da conta conectada."),
            Operation("customers.list", "Clientes", "Listar clientes", "Retorna uma página de clientes do Asaas."),
            Operation("customers.get", "Clientes", "Consultar cliente", "Consulta um cliente pelo ID do Asaas.", requiresResourceId: true),
            Operation("payments.list", "Cobranças", "Listar cobranças", "Retorna uma página de cobranças, incluindo boleto, Pix e cartão."),
            Operation("payments.get", "Cobranças", "Consultar cobrança", "Consulta uma cobrança pelo ID do Asaas.", requiresResourceId: true),
            Operation("payments.status", "Cobranças", "Consultar situação", "Consulta a situação atual de uma cobrança.", requiresResourceId: true),
            Operation("payments.identification-field", "Boleto", "Consultar linha digitável", "Recupera a identificação bancária de um boleto.", requiresResourceId: true),
            Operation("payments.pix-qrcode", "Pix", "Consultar QR Code Pix", "Recupera o QR Code dinâmico e o código copia e cola.", requiresResourceId: true),
            Operation("pix.keys.list", "Pix", "Listar chaves Pix", "Consulta as chaves Pix cadastradas na conta."),
            Operation("webhooks.list", "Webhooks", "Listar webhooks", "Audita URLs, eventos e o estado das filas de webhook configuradas."),
            Operation("invoices.list", "Notas fiscais", "Listar notas fiscais", "Retorna uma página de NFS-e cadastradas no Asaas."),
            Operation("invoices.get", "Notas fiscais", "Consultar nota fiscal", "Consulta uma NFS-e pelo ID do Asaas.", requiresResourceId: true),
            Unavailable("payments.create", "Cobranças", "Criar cobrança", "POST", GatewayDiagnosticRisk.ProductionMutation,
                "A credencial atual é de produção. A criação será habilitada após o contrato de confirmação e idempotência do laboratório."),
            Unavailable("credit-card.tokenize", "Cartão", "Tokenizar cartão", "POST", GatewayDiagnosticRisk.Sensitive,
                "Dados brutos de cartão não podem transitar pelo Endpoints; use tokenização compatível com PCI no navegador."),
            Unavailable("credit-card.pay", "Cartão", "Pagar com cartão tokenizado", "POST", GatewayDiagnosticRisk.ProductionMutation,
                "Requer token de cartão, confirmação reforçada e trilha de idempotência."),
            Unavailable("pix.transfer", "Pix", "Enviar Pix", "POST", GatewayDiagnosticRisk.ProductionMutation,
                "Transferências reais permanecem bloqueadas até existir confirmação reforçada e limite operacional."),
            Unavailable("subscriptions.manage", "Assinaturas", "Gerenciar assinaturas", "POST", GatewayDiagnosticRisk.ProductionMutation,
                "Operações de assinatura serão adicionadas ao dispatcher após os contratos de criação e cancelamento."),
            Unavailable("payment-links.manage", "Links de pagamento", "Gerenciar links", "POST", GatewayDiagnosticRisk.ProductionMutation,
                "Operações de link serão adicionadas ao dispatcher após os contratos de criação e cancelamento."),
            Unavailable("invoices.manage", "Notas fiscais", "Agendar, autorizar ou cancelar NFS-e", "POST", GatewayDiagnosticRisk.ProductionMutation,
                "Os métodos já existem no gateway, mas o laboratório exige confirmação reforçada antes de expor mutações de produção.")
        };

    IReadOnlyList<GatewayDiagnosticOperation> IGatewayDiagnosticsGateway.DiagnosticOperations
        => Operations;

    async Task<GatewayDiagnosticProviderResult?> IGatewayDiagnosticsGateway.ExecuteDiagnosticAsync(
        GatewayDiagnosticRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operation = Operations.FirstOrDefault(item =>
            string.Equals(item.Code, request.OperationCode, StringComparison.OrdinalIgnoreCase));
        if (operation == null || !operation.Available)
        {
            throw new InvalidOperationException("The requested Asaas gateway operation is not executable.");
        }

        var resourceId = operation.RequiresResourceId
            ? RequireDiagnosticResourceId(request.ResourceId)
            : null;
        var offset = Math.Max(0, request.Offset);
        var limit = Math.Min(100, Math.Max(1, request.Limit));
        var relativePath = operation.Code switch
        {
            "authentication" => "myAccount/commercialInfo/",
            "customers.list" => $"customers?offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}",
            "customers.get" => $"customers/{Uri.EscapeDataString(resourceId!)}",
            "payments.list" => $"payments?offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}",
            "payments.get" => $"payments/{Uri.EscapeDataString(resourceId!)}",
            "payments.status" => $"payments/{Uri.EscapeDataString(resourceId!)}/status",
            "payments.identification-field" => $"payments/{Uri.EscapeDataString(resourceId!)}/identificationField",
            "payments.pix-qrcode" => $"payments/{Uri.EscapeDataString(resourceId!)}/pixQrCode",
            "pix.keys.list" => $"pix/addressKeys?offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}",
            "webhooks.list" => $"webhooks?offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}",
            "invoices.list" => $"invoices?offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}",
            "invoices.get" => $"invoices/{Uri.EscapeDataString(resourceId!)}",
            _ => throw new InvalidOperationException("The requested Asaas gateway operation is not mapped.")
        };

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, relativePath)),
            context,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return new GatewayDiagnosticProviderResult
        {
            HttpStatusCode = (int)response.StatusCode,
            Payload = document.RootElement.Clone()
        };
    }

    private static GatewayDiagnosticOperation Operation(
        string code,
        string category,
        string title,
        string description,
        bool requiresResourceId = false)
        => new()
        {
            Code = code,
            Category = category,
            Title = title,
            Description = description,
            RequiresResourceId = requiresResourceId
        };

    private static GatewayDiagnosticOperation Unavailable(
        string code,
        string category,
        string title,
        string method,
        GatewayDiagnosticRisk risk,
        string note)
        => new()
        {
            Code = code,
            Category = category,
            Title = title,
            Description = note,
            Method = method,
            Risk = risk,
            Available = false,
            AvailabilityNote = note
        };

    private static string RequireDiagnosticResourceId(string? resourceId)
    {
        var value = resourceId?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160)
        {
            throw new ArgumentException("A valid Asaas resource identifier is required.", nameof(resourceId));
        }

        return value;
    }
}
