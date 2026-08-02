# Sufficit Gateway Asaas

Integração HTTP tipada da Sufficit com a API Asaas.

`AsaasGateway` é a fachada geral do provedor. As capacidades atuais são
boletos (`IBankSlipGateway` e `IBankSlipProviderDiagnosticsGateway`) e NFS-e
(`IAsaasInvoiceGateway`), sempre usando o provider persistido `asaas`.

## Responsabilidades

- compartilhar autenticação, cliente HTTP, configuração e credenciais entre
  todas as capacidades Asaas;
- emitir, consultar e cancelar boletos;
- localizar clientes por CPF/CNPJ antes de criá-los;
- reconciliar cobranças pela referência externa antes de repetir uma emissão;
- agendar, consultar, listar, atualizar, autorizar e cancelar NFS-e;
- normalizar estados e erros próprios do Asaas;
- oferecer consultas tipadas e somente leitura para a console de diagnóstico.

## Idempotência e segurança

O `BankSlipId` é enviado como `externalReference` e consultado antes de uma
criação, reduzindo o risco de cobrança duplicada. Não há failover automático.

O ambiente e a habilitação do provider são definidos pelas preferências do
tenant. A autorização excepcional de uma emissão em produção pertence ao host
e à interface administrativa, não ao gateway.

## Configuração

O host registra o gateway e a infraestrutura neutra separadamente:

```csharp
services.AddSufficitGatewayInfrastructure(configuration);
services.AddSufficitBankSlipInfrastructure(configuration);
services.AddSufficitGatewayAsaas(configuration);
```

As opções e credenciais gerais ficam em `Sufficit:Gateway:Asaas`:

```json
{
  "Sufficit": {
    "Gateway": {
      "Asaas": {
        "SandboxBaseAddress": "https://api-sandbox.asaas.com/v3/",
        "ProductionBaseAddress": "https://api.asaas.com/v3/",
        "UserAgent": "Sufficit-Gateway-Asaas/2.0 (.NET)",
        "Timeout": "00:00:30",
        "Credentials": {}
      }
    }
  }
}
```

A API key não pertence a este repositório nem ao payload das filas. O host
resolve uma referência opaca por `IGatewayCredentialResolver` a partir da
configuração protegida.

## NFS-e

`IAsaasInvoiceGateway` cobre os endpoints `/v3/invoices` de listagem,
consulta, agendamento, atualização, autorização e cancelamento. Os modelos
mantêm propriedades adicionais do provedor por `JsonExtensionData`, evitando
perda de dados quando a API evoluir. O objeto tributário permanece tipado como
JSON porque sua composição depende do regime e do município, inclusive regras
da NT-007.

## Validação

```bash
dotnet test tests/Sufficit.Gateway.Asaas.Tests.csproj
```

Os testes usam um `HttpMessageHandler` controlado e não acessam contas reais.
