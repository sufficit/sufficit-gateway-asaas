# Recurring initial charge and monthly price

User requested recurring credit card first/default, an initial charge including prorated items and the agreed monthly price thereafter, without an end date.

Validation: 189 Checkout tests (including real Razor rendering of first-option order and distinct price consent), 14 EndPoints billing tests, 65 Asaas gateway tests. Release publishes succeeded with existing dependency/nullability warnings. No customer charge/payment/subscription was created during validation.

Production: Checkout and primary EndPoints installed, health checks passed, installed API DLL hashes matched the published artifact; appsettings preserved. Browser selection check was blocked by a pre-existing pending one-off card intent; do not create/cancel a payment for UI validation. Read-only review at 390/1280 px confirmed the pending one-off card intent is preserved, methods locked against duplicate payment, no overflow or JavaScript errors. Eligible recurring styling checked only in an isolated DOM preview; eligibility/consent rendering covered by HtmlRenderer test.

Activation is BLOCKED: Asaas production safe empty-body tokenization permission probe returned 403 forbidden, asking to contact the account manager. Documentation requires tokenization enabled to change a card subscription price. VariableRecurringAmountEnabled defaults false and is checked in quote and reservation. Production quote verified initial397.60/monthly389.60/next22-Nov/CanAuthorize=false. Do not enable until Asaas grants the capability and provider price transition is validated. No initial397.60 subscription was started.

References: https://docs.asaas.com/reference/atualizar-assinatura-existente and https://docs.asaas.com/reference/atualizar-cobranca-existente.

## Component changes

Reconciliation binds checkout/customer/subscription and rejects finite recurring terms or unexplained paid values. It updates subscription value with updatePendingPayments=false and targets only pre-generated future pending charges; it never reprices the first pending charge. Readback confirms the monthly schedule. Repeating after success does not write; errors retain evidence for worker retry.
