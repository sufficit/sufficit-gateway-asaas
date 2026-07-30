# Sufficit Gateway Asaas

Integração HTTP tipada da Sufficit com a API Asaas para emissão, consulta e
cancelamento de boletos.

O projeto implementa `IBankSlipGateway` e
`IBankSlipProviderDiagnosticsGateway` para o provider persistido `asaas`.

## Responsabilidades

- emitir, consultar e cancelar boletos pelo cliente HTTP tipado;
- localizar clientes por CPF/CNPJ antes de criá-los;
- reconciliar cobranças pela referência externa antes de repetir uma emissão;
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
services.AddSufficitBankSlipGatewayInfrastructure(configuration);
services.AddSufficitAsaasBankSlipGateway(configuration);
```

As opções HTTP ficam em `BankSlips:Providers:Asaas`:

```json
{
  "BankSlips": {
    "Providers": {
      "Asaas": {
        "SandboxBaseAddress": "https://api-sandbox.asaas.com/v3/",
        "ProductionBaseAddress": "https://api.asaas.com/v3/",
        "UserAgent": "Sufficit-BankSlips/2.0 (.NET)",
        "Timeout": "00:00:30"
      }
    }
  }
}
```

A API key não pertence a este repositório nem ao payload das filas. O host
resolve uma referência opaca por `IBankSlipCredentialResolver` a partir da
configuração protegida.

## Validação

```bash
dotnet test tests/Sufficit.Gateway.Asaas.Tests.csproj
```

Os testes usam um `HttpMessageHandler` controlado e não acessam contas reais.
