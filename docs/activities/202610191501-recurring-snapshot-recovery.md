# Recuperação da consulta recorrente

Escopo: durante o aceite Sandbox de 19/10, GET payments filtrado por checkoutSession retornou vazio, enquanto GET por cliente devolveu a cobrança confirmada com o checkout correto. Recuperar sem relaxar o vínculo cliente/checkout/assinatura. Nenhuma mudança financeira produtiva autorizada.

1. Concluído: regressão que reproduz filtro vazio e consulta paginada do mesmo cliente.
2. Concluído: fallback somente quando filtro original vazio; selecionar checkout exato e manter validações de identidade.
3. Concluído: testes Gateway e execução real da conciliação na fixture Sandbox; registrar entrega e limitações.

Aceite: preservar primeira cobrança, corrigir mensalidade; repetição sem PUT; outros checkouts/assinaturas não associados. Comandos: dotnet test tests/Sufficit.Gateway.Asaas.Tests.csproj; harness privado Sandbox do Checkout.

Regressões reproduziram a falha antes da correção (2 falhas esperadas). Após fallback: 67 testes Gateway passaram.

Aceite real Sandbox: primeira cobrança R$ 12,34 confirmada e preservada, assinatura R$ 9,87 com próximo vencimento 19/11, repetição com zero novas escritas. Fixture encerrada no Sandbox após aceite. Alteração local não publicada. O aceite integrado Serviços/Hermes permanece no plano do Checkout por bloqueio de compilação e isolamento de ambiente não demonstrado.
