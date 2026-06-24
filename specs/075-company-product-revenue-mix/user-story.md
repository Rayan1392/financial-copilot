# User Story - Company Product Revenue Mix

## Story

As a TahlilApp-AI user,

I want to ask questions about a company's most important products and revenue composition,

so that I can quickly understand which products generate the majority of the company's revenue and evaluate business concentration risk.

## Business Context

Monthly production and sales reports from Noavaran Amin contain product-level details including production quantity, sales quantity, sales rate, and sales amount.

Currently the system can answer total monthly sales questions, but it cannot identify the dominant products that drive company revenue.

This story introduces a derived-data layer that calculates product revenue composition and enables natural-language questions such as:

- مهم‌ترین محصول کچاد چیست؟
- کگل بیشتر از چه محصولی درآمد دارد؟
- ترکیب فروش محصولات فملی را نشان بده
- محصولات اصلی شرکت چیست؟

## Acceptance Criteria

### Data Source

- Only Noavaran Amin monthly production and sales data is used.
- CyclicalWaves data must not be used for product composition calculations.
- Calculations are based on product-level sales amount.

### Revenue Mix Calculation

For each company and reporting month:

RevenueSharePercentage = ProductSalesAmount / TotalCompanySalesAmount * 100

The system calculates:

- Total company sales amount
- Product revenue share percentage
- Product ranking by sales amount
- Dominant product flag

### Persistence

Derived results are persisted in a dedicated database table.

### AI Experience

The AI can answer:

- Most important product
- Top products
- Revenue composition
- Product concentration

### Response Rules

If one or more products exceed 30% of total revenue:

- Return dominant products.

If no product exceeds 30%:

- Return top 3 products by revenue share.

### Explainability

Responses must include:

- Reporting month
- Product name
- Sales amount
- Revenue share percentage

### Performance

AI queries must read from persisted derived data and must not recalculate historical revenue composition in real time.
