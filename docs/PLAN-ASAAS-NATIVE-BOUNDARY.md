# PLAN — Fronteira nativa do gateway Asaas

> **Diretrizes (usuário, 2026-09-23):**
> 1. Os gateways da Sufficit devem ser independentes. O gateway do Asaas trata
>    **somente de coisas do próprio Asaas**.
> 2. As classes referentes ao Asaas — inclusive os contratos que o gateway
>    consome — ficam **dentro do gateway do Asaas**. Ele se torna autocontido.
>    **Sem pacote `Abstractions` novo.**
> 3. `sufficit-base` fica com as **declarações**; o `sufficit-standard` fica
>    com as **implementações** — e o Standard **importa o gateway do Asaas**
>    (`Standard → gateway`, nunca o contrário).
> 4. **O conceito de ambiente (sandbox/produção) é responsabilidade de cada
>    gateway** (correção do usuário): muitos provedores nem têm sandbox,
>    porque o próprio provedor não tem — trata de uma situação diferente.
>    Cada gateway modela o próprio ambiente — ou simplesmente não tem
>    ambiente, quando o provedor não oferece. **A Base não
>    impõe ambiente universal.**
> 5. Implementamos as classes que faltam direto no gateway; isso vai cobrindo
>    as etapas naturalmente.
> 6. O **Checkout** consome o `sufficit-client`, exclusivamente com endpoints
>    privados do `sufficit-endpoints`.

Status: etapa 1 **implementada e testada** (build 0 erros/0 avisos, suíte
54/54); etapas 2–4 permanecem em proposta. Medições de 2026-09-23.

## 1. Problema

`Sufficit.Gateway.Asaas` referencia `Sufficit.Base` (935 arquivos, 38.781
linhas) por dois motivos: implementa contratos de domínio `Finance` que não
são dele, e consome contratos neutros (`GatewayCallContext`, credencial,
resolver, `GatewayEnvironment`) que hoje vivem na Base.

## 2. Estado medido

### 2.1 Acoplamento por namespace

| Namespace importado da Base | Peso | Observação |
| --- | --- | --- |
| `Sufficit.Gateway` (5 arquivos) | 82 linhas | contratos neutros, só BCL |
| `Sufficit.Gateway.Diagnostics` | 135 linhas | só BCL |
| `Sufficit.Finance` | 114 arquivos, 3.134 linhas | declarações de domínio |

### 2.2 Divisão do código do gateway

| Depende de `Sufficit.Finance` | Livre de `Finance` |
| --- | --- |
| `BankSlips.cs` (589), `PixPayments.cs` (523), `Webhooks.cs` (174), `ServiceCollectionExtensions.cs` (42) | `Invoices.cs` (375), `RateLimitCoordinator.cs` (338), `Checkouts.cs` (295), `WebhookSubscriptions.cs` (266), `InvoiceModels.cs` (203), demais |
| **1.328 linhas** | **2.240 linhas** |

### 2.3 Ambiente por provedor — evidências (diretriz 4)

- **`GatewayEnvironment`** (enum na Base) **não é universal**:
  - `sufficit-gateway-abrtelecom`: **zero** usos — provedor sem ambiente;
  - o **Finance declara enum próprio** (`BankSlipProviderEnvironment`) e
    **não** usa `GatewayEnvironment` — as assinaturas de domínio não dependem
    dele;
  - `sufficit-gateway-efi` **já traduz** `BankSlipProviderEnvironment` →
    `GatewayEnvironment` (`EfiGateway.BankSlips.cs:406`) — mapear ambiente de
    domínio para ambiente de provedor é padrão estabelecido;
  - os endereços por ambiente **já são do Asaas**:
    `AsaasGatewayOptions.SandboxBaseAddress` / `ProductionBaseAddress`;
  - `AsaasGateway.BankSlips.cs:367` já traduz `BankSlipGatewayContext` →
    `GatewayCallContext` — a tradução contexto-de-domínio → contexto-de-
    provedor também é padrão estabelecido.

### 2.4 Exclusividade das classes da Base — **falsificada**

`GatewayCallContext`/`GatewayCredential`/`GatewayEnvironment`/
`IGatewayCredentialResolver`/diagnósticos **não são exclusivos do Asaas**.
Consumidores (grep cross-repo, sem bin/obj/worktrees): `gateway-efi`,
`gateway-abrtelecom`, a própria Base (`Finance/IPixPaymentGateway.cs` assina
com `GatewayCallContext`), `standard`, `checkout`, `endpoints` (AsaasInvoices),
`blazor` e `blazor-ai-access` (dashboards).

**Consequência:** as cópias da Base **permanecem** para os demais consumidores;
o gateway Asaas ganha tipos próprios. Não há "mover e deletar da Base".

## 3. Arquitetura

### 3.1 O que fica onde

| Camada | Papel | Conteúdo |
| --- | --- | --- |
| `sufficit-gateway-asaas` | **autocontido, só fala Asaas** | HTTP, credencial, rate limit, NFS-e, checkouts, webhooks Asaas, diagnósticos, **contratos próprios** (incl. ambiente) |
| `sufficit-base` | **declara** | interfaces `Finance` + contratos `Gateway` neutros — **intactos**, servindo efi/abrtelecom/Finance/etc. |
| `sufficit-standard` | **implementa** | adaptadores `asaas` das interfaces Finance; **importa o gateway do Asaas** |
| `sufficit-checkout` | **consumidor da API Sufficit** | via `sufficit-client` + endpoints privados |

### 3.2 Contratos próprios do gateway (aprovados pelas diretrizes 2 e 4)

Dentro do gateway, namespace `Sufficit.Gateway.Asaas`:

- `AsaasGatewayCallContext` — `TenantId`, `CredentialReference`,
  **`AsaasEnvironment`** (`Sandbox`/`Production`) — o ambiente é modelado
  pelo Asaas, para o Asaas;
- `AsaasGatewayCredential`, `AsaasGatewayCredentialException`;
- `IAsaasGatewayCredentialResolver`;
- `IAsaasDiagnosticsGateway` + `AsaasDiagnosticOperation` (substituem, na
  superfície do Asaas, implementar `IGatewayDiagnosticsGateway` da Base).

Coexistência sem colisão: namespaces distintos (`Sufficit.Gateway` ×
`Sufficit.Gateway.Asaas`). As cópias da Base seguem para os demais.

### 3.3 Contratos nativos novos (as classes que faltam)

Chamadas HTTP de `/v3/payments`, `identificationField`, QR Code Pix e
`/v3/customers` hoje vivem dentro de `BankSlips.cs`/`PixPayments.cs`,
misturadas à tradução de domínio. Passam a contratos nativos:

- `IAsaasPaymentGateway` — cobranças, campo de identificação, QR Code Pix;
- `IAsaasCustomerGateway` — clientes.

### 3.4 Implementações que saem para o Standard

Destino: `sufficit-standard/src/Finance/BankSlip/Infrastructure/Asaas/`, ao
lado de `BankSlipGatewayResolver` e `AddSufficitBankSlipInfrastructure`.
O Standard ganha `ProjectReference` ao gateway e os adaptadores mapeiam:

```
BankSlipGatewayContext (Finance, Base)          AsaasGatewayCallContext (gateway)
  TenantId ───────────────────────────────────►  TenantId
  CredentialReference ─────────────────────────►  CredentialReference
  Environment: BankSlipProviderEnvironment ────►  AsaasEnvironment
```

Tradução de enum ambiente-de-domínio → ambiente-de-provedor: mesmo padrão
que o efi já aplica hoje (`EfiGateway.BankSlips.cs:406`).

O `ServiceCollectionExtensions` do gateway registra **somente** interfaces
Asaas; o registro dos adaptadores Finance passa para o Standard.

### 3.5 Resultado

Gateway do Asaas com **zero referências a `Sufficit.Base`** — autocontido,
sem pacote novo, sem coreografia de release entre repositórios, e com o
ambiente modelado como propriedade do provedor (não da plataforma).

## 4. Checkout — migração para o `sufficit-client`

### 4.1 Situação medida

Blazor Server interativo, sem autenticação; referencia só
`Sufficit.Gateway.Asaas`, `Sufficit.Blazor.UI`, `Microsoft.Data.Sqlite`. Usa
`IPixPaymentGateway` (3×), `IBankSlipGateway` (2×), `IAsaasCheckoutGateway`
(2×), `IAsaasWebhookGateway` (2×) — transitivo pelo gateway.

### 4.2 Separação público/privado já existe nos endpoints

`PublicBankSlipController` (`public/finance/bankslips`, `[AllowAnonymous]`) ×
`BankSlipController` (`Finance/BankSlip`, `[Authorize]`). Precedente
máquina-a-máquina: `MeteredSalesAuthenticationHandler`
(`X-Cloud-Mobile-Billing-Key`).

### 4.3 Dois bloqueios reais

**(a) Autenticação** — client resolve token via `HttpContextTokenProvider`
(`ClaimTypes.AccessToken`): pressupõe usuário logado; Checkout é anônimo.
Precisa de credencial de aplicação.
**(b) Cobertura** — endpoints não expõem Pix/checkout e o client não tem
seção de Pix: criar endpoints privados + seções é **o maior item do plano**.

### 4.4 `sufficit-client-private`

1. Pacote `Sufficit.Client.Private` (fronteira explícita) — **recomendada**;
2. Seções no próprio client com registro distinto (convenção).

## 5. Etapas (ordem de release)

1. **Contratos nativos** no gateway: `IAsaasPaymentGateway`,
   `IAsaasCustomerGateway` (extrair HTTP + modelos de
   `BankSlips.cs`/`PixPayments.cs`). Isolada, sem quebra.
   **✅ CONCLUÍDA (2026-09-23):** entregues `IAsaasPaymentGateway`
   (`CreatePaymentAsync`, `GetPaymentAsync`, `ListPaymentsAsync`,
   `GetPaymentStatusAsync`, `GetIdentificationFieldAsync`,
   `GetPixQrCodeAsync`, `DeletePaymentAsync`) e `IAsaasCustomerGateway`
   (`CreateCustomerAsync`, `UpdateCustomerAsync` — PUT validado na doc
   oficial —, `GetCustomerAsync` herdado do parcial Invoices,
   `ListCustomersAsync`), com modelos tipados (`AsaasPaymentModels.cs`,
   `AsaasCustomerModels.cs`), parciais `AsaasGateway.Payments.cs` /
   `AsaasGateway.Customers.cs` sobre o núcleo existente, registro no
   `AddSufficitGatewayAsaas` e 16 testes novos (suíte total 54/54; helper
   `EnsureInvoiceSuccessAsync` generalizado para
   `EnsureSuccessAsync(response, label, ct)`). Os parciais Finance
   (`BankSlips.cs`/`PixPayments.cs`) **continuam com o HTTP próprio** — a
   reescrita sobre os contratos nativos acontece na etapa 3, quando as
   implementações saírem para o Standard.
2. **Migrar o Checkout** para o client (endpoints privados, credencial de
   aplicação, remover referência ao gateway). Maior item; independe da 1.
3. **Movimento atômico no gateway + Standard:** implementações
   `BankSlip*`/`PixPayment*`/parser de webhook saem para o Standard como
   adaptadores; gateway define os contratos próprios (3.2), troca os usings,
   **remove a referência a `Sufficit.Base`**; endpoints (AsaasInvoices) e
   páginas Asaas do blazor passam a consumir os tipos do Asaas;
   `ServiceCollectionExtensions` registra só interfaces Asaas. *Atômico
   porque enquanto `BankSlips`/`PixPayments` existirem no gateway, a
   referência à Base não pode sair.*
4. **Validar** consumidores (`background`, `endpoints`) + migrar testes
   (`AsaasGatewayBankSlipTests`, `AsaasGatewayPixPaymentTests`).

## 6. Riscos

- Etapa 3 move 1.328 linhas com comportamento financeiro real
  (idempotência por `externalReference`, mapeamento de status, parser de
  webhook): movimento **sem alteração semântica**, testes migrados junto.
- Dois contextos coexistem no Standard (`Sufficit.Gateway` ×
  `Sufficit.Gateway.Asaas`): mapeamento **explícito** nos adaptadores —
  decisão aprovada (diretrizes 2 e 4), não acidente.
- Blazor ProviderLab consome `GatewayDiagnosticOperation` da Base: o
  adaptador do Standard traduz `AsaasDiagnosticOperation` →
  `GatewayDiagnosticOperation` para o runtime de diagnóstico.
- Três pontos de composição chamam `AddSufficitGatewayAsaas`:
  `background/…/RuntimeServiceCollectionExtensions.cs:55`,
  `checkout/…/Program.cs:97`,
  `endpoints/src/Gateway/GatewayServiceCollectionExtensions.cs:64`.
- Etapa 2 cria superfície de API nova para pagamento — revisão de segurança
  própria (Checkout anônimo e público).

## 7. O que este plano não faz

- Não deleta da Base `GatewayCallContext`/`GatewayCredential`/
  `GatewayEnvironment`/resolver/diagnósticos — continuam servindo efi,
  abrtelecom, Finance e demais consumidores (exclusividade falsificada).
- Não move declarações `Finance` da Base.
- Não altera comportamento da API Asaas nem contratos públicos de NFS-e.
- Não trata do plugin `sufficit-ai-genius/plugins/asaas`: com o gateway
  autocontido o plugin pode referenciá-lo direto; restam adaptar
  credencial/Vault, tradução `AsaasGatewayException` →
  `ToolInvocationResult` (códigos já alinhados) e `fiscalInfo/services`.
  Plano próprio.
