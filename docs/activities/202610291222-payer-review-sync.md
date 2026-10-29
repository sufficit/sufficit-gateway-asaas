# Dados revisados para checkout hospedado

AsaasCheckoutCustomerData permite enviar nome, CPF/CNPJ, e-mail, telefone e endereço revisados pelo cliente. Validação recusa campos incompletos e customer simultâneo com customerData. Serialização usa cpfCnpj e phone conforme API. Nenhum dado de cartão é adicionado.

Resolver customer por checkout exato permite correção do documento sem perder vínculo financeiro. Paginação inválida, método divergente e associação a outro checkout/cliente são rejeitados. A assinatura e os valores continuam sendo conferidos pelas rotinas recorrentes existentes.

Validação: 71 testes aprovados contra dependências publicadas. Sandbox rejeitou prefill parcial sem endereço; customerData completo aceito e exibido com campos bloqueados no Asaas, justificando a revisão editável anterior na Sufficit. Sessão de teste cancelada sem pagamento. Gateway publicado na API preservando bibliotecas da publicação concorrente.

[Contrato do fluxo](https://github.com/sufficit/sufficit-checkout/blob/main/docs/PAYER-REVIEW-SYNC.md).
