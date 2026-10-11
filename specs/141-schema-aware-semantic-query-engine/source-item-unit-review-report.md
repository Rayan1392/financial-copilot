# Feature 141 Source Item Unit Review

Policy version: `financial-statement-item-units-v1`
Source item definitions inspected: 296

| Classification | Count |
|---|---:|
| MonetaryStatementItem | 284 |
| MonetaryPerShareItem | 8 |
| RequiresSemanticReview | 4 |

## Per-share items

| Provider | Statement | Source item ID | Source catalog ID | Title | Effective unit | Rule |
|---|---|---:|---|---|---|---|
| NoavaranCurrentApi | BalanceSheet | 271 | `e51d1dde-6a7b-48d6-9d79-8f417a088e1f` | ارزش دفتری هر سهم | IRRPerShare | financial-statement-unit-policy-v1:per-share-title-rule |
| NoavaranCurrentApi | IncomeStatement | 160 | `2146da26-b28e-4d04-b50c-f49fc9b70e61` | سود خالص هر سهم  | IRRPerShare | financial-statement-unit-policy-v1:per-share-title-rule |
| NoavaranCurrentApi | IncomeStatement | 161 | `3cafc7d3-b0ab-4341-85ef-ebcbc9d60528` | سود عملیاتی هر سهم  | IRRPerShare | financial-statement-unit-policy-v1:per-share-title-rule |
| NoavaranCurrentApi | IncomeStatement | 162 | `2dd8a720-0cfe-4a43-a7ff-971b4e5bc366` | سود هر سهم قبل از کسر مالیات  | IRRPerShare | financial-statement-unit-policy-v1:per-share-title-rule |
| NoavaranCurrentApi | IncomeStatement | 163 | `49264c8b-4348-4109-9334-6533d12ccaa0` | حصه هر سهم از جمع درآمدها | IRRPerShare | financial-statement-unit-policy-v1:per-share-title-rule |
| NoavaranCurrentApi | IncomeStatement | 168 | `515f20cd-549b-4163-a821-2e52f0bead86` | سود خالص تلفیقی هر سهم | IRRPerShare | financial-statement-unit-policy-v1:per-share-title-rule |
| NoavaranCurrentApi | IncomeStatement | 202 | `dc53f4fe-fd63-4783-8af7-092c1cd1cf41` | حصه هر سهم از جمع درآمدهای مشاع | IRRPerShare | financial-statement-unit-policy-v1:per-share-title-rule |
| NoavaranCurrentApi | IncomeStatement | 317 | `1d244aae-7e5d-44e6-9ec4-c1659828856d` | سود خالص هر سهم - کدال | IRRPerShare | financial-statement-unit-policy-v1:per-share-title-rule |

## Requires semantic review

These items remain without an effective unit until reviewed.

| Provider | Statement | Source item ID | Source catalog ID | Title | Raw unit | Reason |
|---|---|---:|---|---|---|---|
| NoavaranCurrentApi | BalanceSheet | 99 | `5e03f84d-b77d-4549-8c2e-caadbe2c9f1f` | نتیجه برابری نرخ ارز (اندوخته تسعیر ارز) | N/A | financial-statement-unit-policy-v1:nonmonetary-or-ambiguous-title |
| NoavaranCurrentApi | BalanceSheet | 286 | `67731fc6-cc77-42aa-a78e-2b2fd284778f` | نوع دوره مالی | N/A | financial-statement-unit-policy-v1:nonmonetary-or-ambiguous-title |
| NoavaranCurrentApi | CashFlow | 35 | `f0a6922b-ac3f-4166-b313-656377568843` | تاثیر تغییرات نرخ ارز | N/A | financial-statement-unit-policy-v1:nonmonetary-or-ambiguous-title |
| NoavaranCurrentApi | IncomeStatement | 153 | `e98e6c00-7204-44b1-a279-bbbb68781d3c` | تعداد سهام شرکت - به هزار سهم  | N/A | financial-statement-unit-policy-v1:nonmonetary-or-ambiguous-title |
