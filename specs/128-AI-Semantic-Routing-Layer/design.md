# Feature 128 — AI Semantic Routing Layer

## Status

Idea / Discovery

## Title

AI Semantic Routing Layer for Natural Language Understanding and Capability Routing

## Objective

ایجاد یک لایه هوشمند بین پرسش کاربر و سیستم Orchestration که بتواند مفهوم و هدف کاربر را از زبان طبیعی استخراج کرده و بدون وابستگی به ساختار جمله، کلمات کلیدی یا گرامر ثابت، درخواست را به Capability و Tool مناسب هدایت کند.

هدف این Feature جلوگیری از وابستگی سیستم به ruleهای سخت‌گیرانه semantic matching و فراهم کردن تجربه‌ای مشابه تعامل طبیعی با یک دستیار هوشمند است.

## Problem Statement

در معماری فعلی، بخشی از تشخیص مسیر درخواست بر اساس:

* keyword matching
* phrase matching
* deterministic rules
* grammar patterns

انجام می‌شود.

این روش در سناریوهای ساده مناسب است، اما با افزایش تعداد قابلیت‌ها و تنوع زبان کاربران، مشکلات زیر ایجاد می‌شود:

* کاربر مجبور می‌شود پرسش را با ساختار خاصی بیان کند.
* یک مفهوم واحد با جملات مختلف ممکن است به مسیرهای متفاوت هدایت شود.
* تعداد ruleهای semantic به مرور افزایش غیرقابل کنترل پیدا می‌کند.
* اضافه شدن هر Feature جدید باعث افزایش احتمال conflict بین capabilityها می‌شود.

## Example Problem

کاربر ممکن است درخواست زیر را بپرسد:

```
نماد کگهر را با کگل مقایسه کن
```

یا:

```
کگهر و کگل رو کنار هم بذار ببین کدوم بهتره
```

یا:

```
بین گهر زمین و گل گهر از نظر ارزندگی کدوم جذاب‌تره؟
```

همه این درخواست‌ها یک intent دارند:

```
Compare two companies
```

اما نباید سیستم برای هر نوع جمله یک rule جدید داشته باشد.

# Proposed Solution

ایجاد یک Semantic Routing Layer مبتنی بر ترکیب:

* Deterministic Rules
* LLM-based Intent Classification
* Entity Extraction
* Validation Layer
* Capability Registry

Architecture Concept:

```
User Query
     |
     v
Query Understanding Layer
     |
     +----------------+
     |                |
Entity Extraction   Intent Classification
     |                |
     +----------------+
              |
              v
       Semantic Frame
              |
              v
      Validation / Policy Layer
              |
              v
      Capability Router
              |
              v
       Tool Calling / Agents
              |
              v
          Final LLM Response
```

# Semantic Frame Concept

خروجی لایه Semantic Routing نباید متن باشد، بلکه یک ساختار استاندارد باشد.

Example:

```json
{
  "intent": "company_comparison",
  "capability": "symbol_pair_within_industry",
  "entities": [
    {
      "type": "company",
      "value": "کگهر"
    },
    {
      "type": "company",
      "value": "کگل"
    }
  ],
  "comparisonType": "peer",
  "confidence": 0.95
}
```

# Design Principles

## 1. User language should be unrestricted

سیستم نباید کاربر را مجبور کند از عبارت‌هایی مانند:

* "مقایسه کن"
* "با"
* "و"
* "در گروه خودش"

استفاده کند.

باید intent را درک کند.

## 2. LLM should understand, not directly execute

LLM نباید مستقیماً تصمیم نهایی Tool Calling را بگیرد.

مدل پیشنهادی:

```
LLM Semantic Understanding
          |
          v
Structured Intent
          |
          v
Deterministic Validation
          |
          v
Tool Execution
```

## 3. Rules are for validation, not understanding

Ruleها برای موارد زیر استفاده شوند:

* جلوگیری از خطا
* validation
* business constraints
* security checks

نه برای فهم زبان طبیعی.

# Initial Scope (Future)

این Feature در آینده می‌تواند شامل موارد زیر باشد:

## Intent Classification

تشخیص هدف کاربر:

Examples:

* company_lookup
* company_comparison
* financial_statement_request
* monthly_sales_analysis
* ranking_request
* portfolio_analysis

## Entity Understanding

تشخیص موجودیت‌ها:

* شرکت
* نماد
* صنعت
* صندوق
* شاخص
* تاریخ
* معیار مالی

## Capability Mapping

تبدیل intent به capability:

Example:

```
User:
کگهر و کگل رو مقایسه کن

Semantic Frame:

intent:
company_comparison

entities:
کگهر
کگل

capability:
symbol_pair_within_industry
```

## Confidence Handling

در صورت عدم اطمینان:

```
confidence < threshold
```

سیستم می‌تواند:

* سؤال تکمیلی بپرسد.
* چند مسیر احتمالی پیشنهاد دهد.
* از fallback استفاده کند.

# Integration Idea

این لایه باید قبل از orchestration فعلی قرار گیرد:

Current:

```
User Query
    |
Deterministic Capability Interpreter
    |
Capability
    |
Tool
```

Future:

```
User Query
    |
Semantic Routing Layer
    |
Semantic Frame
    |
Capability Router
    |
Tool / Agent
```

# Migration Strategy

این Feature نباید باعث بازنویسی کامل سیستم فعلی شود.

مهاجرت پیشنهادی:

Phase 1:

* اضافه کردن Semantic Router به‌صورت fallback.
* فقط زمانی استفاده شود که deterministic routing confidence پایین دارد.

Phase 2:

* انتقال تدریجی capabilityهای پیچیده به Semantic Router.

Phase 3:

* تبدیل Semantic Router به لایه مرکزی تمام Agentها و Featureها.

# Success Criteria

موفقیت این Feature زمانی است که:

* کاربر بتواند با زبان طبیعی آزاد سؤال بپرسد.
* تعداد ruleهای semantic کاهش پیدا کند.
* Featureهای جدید بدون اضافه کردن ده‌ها pattern جدید قابل اضافه شدن باشند.
* مسیر انتخاب شده قابل توضیح و audit باشد.
* رفتار سیستم مستقل از جمله‌بندی کاربر باشد.

# Notes

این Feature از تجربه Feature 125 و مشکلات semantic routing استخراج شده است.

هدف آن جایگزینی کامل rule-based system نیست؛ هدف ایجاد یک لایه هوشمند برای فهم intent و کاهش وابستگی به grammarهای ثابت است.
