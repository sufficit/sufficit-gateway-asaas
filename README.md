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
- limitar a concorrência de `GET`, manter uma reserva local da cota e observar
  os cabeçalhos dinâmicos `RateLimit-*`;
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
        "MaxConcurrentGetRequests": 40,
        "QuotaLimit": 25000,
        "QuotaReserve": 5000,
        "EnforceLocalQuotaLimit": true,
        "Credentials": {}
      }
    }
  }
}
```

A API key não pertence a este repositório nem ao payload das filas. O host
resolve uma referência opaca por `IGatewayCredentialResolver` a partir da
configuração protegida.

## Limites da API

O pipeline HTTP central conta cada chamada realmente admitida e impede que uma
instância ultrapasse 40 consultas `GET` simultâneas. A janela local permite
20.000 chamadas por credencial a cada 12 horas com a configuração padrão,
reservando 5.000 das 25.000 documentadas pelo Asaas para outros consumidores.

Após cada resposta, `RateLimit-Limit`, `RateLimit-Remaining`,
`RateLimit-Reset` e `Retry-After` atualizam um bloqueio preventivo. Respostas
`429`, ou `403` acompanhadas de reset, suspendem novas chamadas antes de chegar
ao provedor. `IAsaasRateLimitMonitor` expõe o estado observado, o consumo local
e o tempo de nova tentativa.

A cota local é deliberadamente identificada como estimativa: ela não enxerga
outros processos, o n8n ou chamadas manuais da mesma conta e reinicia junto com
o processo. Coordenação exata entre instâncias exige um armazenamento atômico
compartilhado (por exemplo, Redis) implementado no host/worker.

Referência: [limites oficiais da API Asaas](https://docs.asaas.com/reference/rate-e-quota-limit).

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
