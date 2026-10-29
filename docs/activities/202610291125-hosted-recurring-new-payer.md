# Checkout recorrente sem cliente previamente cadastrado

Validação admite customer nulo no checkout mensal, mantendo restrição a cartão e
validação de IDs quando enviados. O Asaas coleta os dados do pagador na página
hospedada; customer/customerData nulos não são enviados.
Referência: https://docs.asaas.com/docs/como-informar-os-dados-do-cliente.

Regressão reproduzida antes (ArgumentException por ausência de customer); após
correção, 68 testes passaram. DLL compilada contra dependências instaladas e
publicada junto ao EndPoints em eveo-apps. Nenhum dado de cartão informado.
Evidências privadas: /mnt/workspaces/sufficit/tmp/recurring-recovery-20261029.
