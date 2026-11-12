# Regra de datas brasileiras dos provedores

Pedido do mantenedor em 12/11/2026: documentar, inclusive inline, que Efí e Asaas
usam GMT−3/Brasília quando o fuso não vier especificado. Respeitar qualquer fuso
explícito, manter vencimentos civis sem deslocamento e não substituir datas
bancárias pelo relógio de processamento.

Escopo concluído: README e comentários nos leitores; nenhuma alteração de lógica,
dados ou publicação. Verificação do conteúdo executável antes/depois, removendo
apenas linhas vazias e comentários de linha: idêntico em todos os arquivos tocados.
`git diff --check` e referências locais conferidos. Testes de runtime não executados:
esta entrega é somente documental.

Estado encontrado e registrado: Os leitores atuais de webhooks de pagamentos/notas, Pix e checkout ainda usam
`AssumeUniversal` quando o offset está ausente. Esses pontos estão marcados inline
como pendência de implementação; esta documentação não os corrigiu.

A documentação cobre os dois gateways. As pendências de parser acima continuam
explícitas e não equivalem a correções implementadas. Commit/push na main após
verificação; nenhuma worktree criada.
