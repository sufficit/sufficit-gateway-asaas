# Recuperação dos contratos de pagamento com cartão Asaas

## Objetivo

Publicar os contratos e o método de cartão Asaas já preparados localmente e consumidos pelos controladores privados de Checkout em `sufficit-endpoints`.

## Estado inicial

Depois da publicação de `Sufficit.Base 1.26.928.59`, o CI de Endpoints `36364596990` compilou os contratos Checkout, mas falhou com 7 erros CS0246/CS1061: `AsaasCreditCardPaymentRequest`, `AsaasCreditCard`, `AsaasCreditCardHolderInfo`, `IAsaasPaymentGateway.PayWithCreditCardAsync`, `AsaasPaymentCallback`, `AsaasPaymentCreateRequest.Callback` e `AsaasCustomer.ExternalReference`. Os tipos e membros já existiam em alterações locais não publicadas deste repositório.

## Alterações incluídas

- Adicionar o método `PayWithCreditCardAsync` à interface e à implementação do gateway.
- Publicar os modelos transitórios de cartão, titular e callback de retorno HTTPS.
- Permitir timeout estendido somente para o pagamento com cartão.
- Expor `ExternalReference` no modelo Asaas de cliente usado pelos invoices.
- Adicionar testes de serialização e chamada do endpoint nativo de cartão/callback.

## Escopo preservado

As alterações locais de filtro de invoices (`effectiveDate[Ge]/[Le]`), o teste correspondente e o plano `PLAN-ASAAS-NATIVE-BOUNDARY.md` ficam fora deste commit.

## Validação e acompanhamento

- `rtk dotnet restore ./tests/Sufficit.Gateway.Asaas.Tests.csproj`: 5 projetos, 0 erros.
- `rtk dotnet test ./tests/Sufficit.Gateway.Asaas.Tests.csproj --configuration Release --no-restore`: 56 testes passaram, 16 avisos existentes de nulabilidade em contratos Base.
- O push dispara publicação NuGet automática. A validação final do CI de Endpoints será executada depois que a nova versão do pacote estiver publicada.
