---
status: accepted
---

# Valores de Property em JSONB e Property Definitions da Account

Os Types e Properties são definidos pelo usuário (ontologia flexível), então os
valores ficam em JSONB com chave `property_id`, validados por Type na camada de
aplicação. Isso é uma **exceção registrada à regra DB-008** dos padrões. As
Property Definitions pertencem à Account e são anexadas a vários Types (N:N).
Assim um valor sobrevive quando o Node troca entre Types que compartilham a
definição, e os valores fora do Type atual ficam guardados como "Outras
propriedades" em vez de serem apagados.

## Considered Options

- **Colunas fixas:** impossível com Types do usuário.
- **EAV:** proibido por DB-008 e mais caro de consultar.
- **Definições por Type:** órfãos a cada troca de Type e propriedades
  duplicadas no Type Resource.

Origem: DA-015 e DA-016, épico `docs/epics/done/e02-nucleo-de-escrita.md`.
