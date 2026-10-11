# Feature 141 Navigation Hierarchy

Catalog version: `1`  
Catalog hash: `cd7da04a59d9ea7cfac96db7f63b3dc03c6fe909dafa417734b3560028eab98c`  
Hierarchy hash: `7b21e46801d0442394c3ce70f9257c4ff4d7e5a38efc6120707d5c5bd466b28b`

Generated from canonical SemanticCatalog YAML. Navigation metadata approval, semantic approval, and query authorization are distinct. Nodes remain review-pending and runtime queries are unauthorized.

| Node ID | Parent | English | Persian | Tables | Navigation review | Query access |
|---|---|---|---|---|---|---|
| `financial-semantic-catalog` | `—` | Financial Semantic Catalog | کاتالوگ معنایی مالی | — | pending-review | not-authorized |
| `domain-company-industry` | `financial-semantic-catalog` | Company & Industry | شرکت و صنعت | tbl_0092e8a0b8fd0d23, tbl_10269adb269eff70, tbl_45c7c30ce87b0cf0, tbl_8646d83cdb350f92, tbl_c03e3950d24db6ed | pending-review | not-authorized |
| `table-tbl_8646d83cdb350f92` | `domain-company-industry` | Companies | شرکت‌ها | tbl_8646d83cdb350f92 | pending-review | not-authorized |
| `table-tbl_c03e3950d24db6ed` | `domain-company-industry` | Markets | بازارها | tbl_c03e3950d24db6ed | pending-review | not-authorized |
| `table-tbl_0092e8a0b8fd0d23` | `domain-company-industry` | Industries | صنایع | tbl_0092e8a0b8fd0d23 | pending-review | not-authorized |
| `table-tbl_10269adb269eff70` | `domain-company-industry` | Industry Groups | گروه‌های صنعتی | tbl_10269adb269eff70 | pending-review | not-authorized |
| `table-tbl_45c7c30ce87b0cf0` | `domain-company-industry` | Trading Instruments | ابزارهای معاملاتی | tbl_45c7c30ce87b0cf0 | pending-review | not-authorized |
| `domain-monthly-activity` | `financial-semantic-catalog` | Monthly Production & Sales | تولید و فروش ماهانه | tbl_4f3dd93b63d1766f, tbl_8e759d70dada6bd8, tbl_8ecd0432a4292998 | pending-review | not-authorized |
| `table-tbl_8e759d70dada6bd8` | `domain-monthly-activity` | Monthly Reports | گزارش‌های ماهانه | tbl_8e759d70dada6bd8 | pending-review | not-authorized |
| `table-tbl_8ecd0432a4292998` | `domain-monthly-activity` | Monthly Product and Service Lines | اقلام محصول و خدمت ماهانه | tbl_8ecd0432a4292998 | pending-review | not-authorized |
| `table-tbl_4f3dd93b63d1766f` | `domain-monthly-activity` | Monthly Activity Trends | روند فعالیت ماهانه شرکت | tbl_4f3dd93b63d1766f | pending-review | not-authorized |
| `domain-financial-statements` | `financial-semantic-catalog` | Financial Statements | صورت‌های مالی | tbl_36036602dbf76362, tbl_4f2ef2d8dd59b46a, tbl_53d51e26e1ca6929, tbl_a7c77d65fb2aaea2 | pending-review | not-authorized |
| `table-tbl_53d51e26e1ca6929` | `domain-financial-statements` | Financial Statement Headers | سرآیند صورت‌های مالی | tbl_53d51e26e1ca6929 | pending-review | not-authorized |
| `table-tbl_4f2ef2d8dd59b46a` | `domain-financial-statements` | Financial Statement Lines | اقلام صورت‌های مالی | tbl_4f2ef2d8dd59b46a | pending-review | not-authorized |
| `table-tbl_36036602dbf76362` | `domain-financial-statements` | Provider Statement Items | اقلام صورت مالی ارائه‌دهنده | tbl_36036602dbf76362 | pending-review | not-authorized |
| `table-tbl_a7c77d65fb2aaea2` | `domain-financial-statements` | Statement Item to Metric Mappings | نگاشت قلم صورت مالی به سنجه | tbl_a7c77d65fb2aaea2 | pending-review | not-authorized |
| `domain-financial-analytics` | `financial-semantic-catalog` | Financial Analytics | تحلیل‌های مالی | tbl_83be994ebc1a8627, tbl_ca0643d8942492ab | pending-review | not-authorized |
| `table-tbl_83be994ebc1a8627` | `domain-financial-analytics` | Derived Financial Metrics | سنجه‌های مالی مشتق‌شده | tbl_83be994ebc1a8627 | pending-review | not-authorized |
| `table-tbl_ca0643d8942492ab` | `domain-financial-analytics` | Industry Relative Valuations | ارزش‌گذاری نسبی صنعت | tbl_ca0643d8942492ab | pending-review | not-authorized |
| `domain-market-data` | `financial-semantic-catalog` | Market Data | داده‌های بازار | tbl_5202a05755e60181, tbl_8e1835aff483a0f4, tbl_aed5e5af50281db4, tbl_b5b50f52c60bb568 | pending-review | not-authorized |
| `table-tbl_aed5e5af50281db4` | `domain-market-data` | Intraday Trade Snapshots | نماهای معاملات درون‌روزی | tbl_aed5e5af50281db4 | pending-review | not-authorized |
| `table-tbl_b5b50f52c60bb568` | `domain-market-data` | Daily Instrument Trades | معاملات روزانه ابزار | tbl_b5b50f52c60bb568 | pending-review | not-authorized |
| `table-tbl_8e1835aff483a0f4` | `domain-market-data` | Intraday Index Snapshots | نماهای درون‌روزی شاخص | tbl_8e1835aff483a0f4 | pending-review | not-authorized |
| `table-tbl_5202a05755e60181` | `domain-market-data` | Daily Index Snapshots | نماهای روزانه شاخص | tbl_5202a05755e60181 | pending-review | not-authorized |
| `shared-dimensions` | `financial-semantic-catalog` | Shared Dimensions & Sources | ابعاد و منابع مشترک | — | pending-review | not-authorized |
| `dimension-dim_company` | `shared-dimensions` | Company Identity | هویت شرکت | — | pending-review | not-authorized |
| `dimension-dim_industry` | `shared-dimensions` | Industry | صنعت | — | pending-review | not-authorized |
| `dimension-dim_monthly_period` | `shared-dimensions` | Monthly Reporting Period | دوره گزارش ماهانه | — | pending-review | not-authorized |
| `dimension-dim_product_identity` | `shared-dimensions` | Company Scoped Product Identity | هویت محصول وابسته به شرکت | — | pending-review | not-authorized |
| `external-source-noavaran-fundamental-index` | `shared-dimensions` | Noavaran Fundamental Index | شاخص بنیادی نوآوران | src_nadpco_company_fundamental_index_values | pending-review | not-authorized |
| `financial-statement-source-items` | `financial-semantic-catalog` | Financial Statement Source Items | اقلام صورت‌های مالی | — | pending-review | not-authorized |
| `source-items-balancesheet` | `financial-statement-source-items` | BalanceSheet | BalanceSheet | — | pending-review | not-authorized |
| `direct-source-item-source_item_b7bcc0f92e19ac4f` | `source-items-balancesheet` | source_item_b7bcc0f92e19ac4f | موجودی نقد | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_293b2dcc7c6e113b` | `source-items-balancesheet` | source_item_293b2dcc7c6e113b | مطالبات از بانک مرکزی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_33df96237c129a9c` | `source-items-balancesheet` | source_item_33df96237c129a9c | مطالبات از بانک‌ها و موسسات اعتباری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1be0e5ed54469946` | `source-items-balancesheet` | source_item_1be0e5ed54469946 | حصه جاری خالص تسهیلات اعطایی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_8b3169c9a3cc9204` | `source-items-balancesheet` | source_item_8b3169c9a3cc9204 | سرمایه‌گذاری کوتاه مدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_7e0cb3a605b19a2e` | `source-items-balancesheet` | source_item_7e0cb3a605b19a2e | حساب‌ها و اسناد دریافتنی تجاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_8d272d35d0731a1b` | `source-items-balancesheet` | source_item_8d272d35d0731a1b | سایر حساب‌ها و اسناد دریافتنی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_0984d745ea740059` | `source-items-balancesheet` | source_item_0984d745ea740059 | طلب از شرکت‌های تابعه و وابسته | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_45009a9fc1b9eee3` | `source-items-balancesheet` | source_item_45009a9fc1b9eee3 | موجودی مواد و کالا | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_33b9bec37b20033e` | `source-items-balancesheet` | source_item_33b9bec37b20033e | سفارشات و پیش پرداخت‌ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_ce6c72f79bf02a4c` | `source-items-balancesheet` | source_item_ce6c72f79bf02a4c | دارایی‌های زیستی غیر مولد | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4900a681f61381ae` | `source-items-balancesheet` | source_item_4900a681f61381ae | دارایی‌های ثابت مشهود | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_63bb2989521efa64` | `source-items-balancesheet` | source_item_63bb2989521efa64 | دارایی‌های نامشهود | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_c91c9eca01d2b3d0` | `source-items-balancesheet` | source_item_c91c9eca01d2b3d0 | سرمایه‌گذاری‌های بلندمدت در اوراق بهادار | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4a4d60608af496f8` | `source-items-balancesheet` | source_item_4a4d60608af496f8 | سرمایه‌گذاری در پروژه‌های ساختمانی بلندمدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_c52e3258cb5204d0` | `source-items-balancesheet` | source_item_c52e3258cb5204d0 | دارایی‌های زیستی مولد | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_6d5d560ee61e637d` | `source-items-balancesheet` | source_item_6d5d560ee61e637d | سرقفلی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_36e6ae41b6eb204a` | `source-items-balancesheet` | source_item_36e6ae41b6eb204a | سایر دارایی‌ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_9088ca5b74e7662b` | `source-items-balancesheet` | source_item_9088ca5b74e7662b | حساب‌ها و اسناد پرداختنی تجاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_76c4076cae3fecb4` | `source-items-balancesheet` | source_item_76c4076cae3fecb4 | سایرحساب‌ها و اسناد پرداختنی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2844e64c217a39e3` | `source-items-balancesheet` | source_item_2844e64c217a39e3 | بدهی به بانک‌ها و موسسات اعتباری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_bcb71da026c99826` | `source-items-balancesheet` | source_item_bcb71da026c99826 | سپرده قرض الحسنه پس انداز و مشابه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_279adbdebb84cbd9` | `source-items-balancesheet` | source_item_279adbdebb84cbd9 | سایر سپرده‌ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4df2639ef29e6123` | `source-items-balancesheet` | source_item_4df2639ef29e6123 | پیش دریافت‌ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a7134e51c701820c` | `source-items-balancesheet` | source_item_a7134e51c701820c | ذخیره مالیات | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_cf1b04d85bd5c2a2` | `source-items-balancesheet` | source_item_cf1b04d85bd5c2a2 | تسهیلات مالی دریافتی کوتاه مدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_0f8574ae4f40dfd8` | `source-items-balancesheet` | source_item_0f8574ae4f40dfd8 | سود سهام پیشنهادی و پرداختنی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_f57cce544c9a0544` | `source-items-balancesheet` | source_item_f57cce544c9a0544 | حساب‌ها و اسناد پرداختنی بلندمدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_72f76950a1ea3bd5` | `source-items-balancesheet` | source_item_72f76950a1ea3bd5 | تسهیلات مالی دریافتی بلند مدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4edbcb33789110ff` | `source-items-balancesheet` | source_item_4edbcb33789110ff | ذخیره مزایای پایان خدمت کارکنان | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_80a227274daf514c` | `source-items-balancesheet` | source_item_80a227274daf514c | سپرده‌های سرمایه‌گذاری مدت دار | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_5301ec5f4312aa36` | `source-items-balancesheet` | source_item_5301ec5f4312aa36 | سرمایه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_35d9c65997a37a72` | `source-items-balancesheet` | source_item_35d9c65997a37a72 | وجوه ناشی از افزایش سرمایه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3a829074bdba8216` | `source-items-balancesheet` | source_item_3a829074bdba8216 | سهام شرکت اصلی در مالکیت شرکت‌های فرعی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_53b5ec4d65909656` | `source-items-balancesheet` | source_item_53b5ec4d65909656 | صرف سهام | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a653b3008aae7697` | `source-items-balancesheet` | source_item_a653b3008aae7697 | اندوخته قانونی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_baf6ed015a3b332a` | `source-items-balancesheet` | source_item_baf6ed015a3b332a | سایر اندوخته‌ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_f7873a68e248db31` | `source-items-balancesheet` | source_item_f7873a68e248db31 | مازاد تجدید ارزیابی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_71afcb3da0ac53eb` | `source-items-balancesheet` | source_item_71afcb3da0ac53eb | سهم اقلیت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3574cd5672f2ac49` | `source-items-balancesheet` | source_item_3574cd5672f2ac49 | سود (زیان) انباشته | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_003133e44248c951` | `source-items-balancesheet` | source_item_003133e44248c951 | حسابها و اسناد دریافتنی بلند مدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_d9c0d57013e121a8` | `source-items-balancesheet` | source_item_d9c0d57013e121a8 | سپرده‌های دیداری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2f6bf5bac4f0832d` | `source-items-balancesheet` | source_item_2f6bf5bac4f0832d | اوراق مشارکت بلندمدت پرداختنی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4d5a74f4d619d47c` | `source-items-balancesheet` | source_item_4d5a74f4d619d47c | حصه بلندمدت پیش دریافت از مشتریان | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_ff7a83528fee6ef5` | `source-items-balancesheet` | source_item_ff7a83528fee6ef5 | سپرده قانونی نزد بانک مرکزی (دارایی غیر جاری) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_dc367d27e801f2c5` | `source-items-balancesheet` | source_item_dc367d27e801f2c5 | مطالبات از دولت (دارایی غیرجاری) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4b15919e25bac895` | `source-items-balancesheet` | source_item_4b15919e25bac895 | نتیجه برابری نرخ ارز (اندوخته تسعیر ارز) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4e2db367a0806964` | `source-items-balancesheet` | source_item_4e2db367a0806964 | مطالبات از بیمه‌گذاران و نمایندگان | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_5cc00a3d52c8288c` | `source-items-balancesheet` | source_item_5cc00a3d52c8288c | مطالبات از بیمه‌گران اتکایی افشایی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_6750773f176ae215` | `source-items-balancesheet` | source_item_6750773f176ae215 | بدهی به بیمه‌گذاران، نمایندگان و کارگزاران | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1211f3cbbb9ad40c` | `source-items-balancesheet` | source_item_1211f3cbbb9ad40c | بدهی به بیمه‌گران اتکایی افشایی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_ee1d6d5736198862` | `source-items-balancesheet` | source_item_ee1d6d5736198862 | جمع دارایی‌های جاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1d9b673c5ea4acd6` | `source-items-balancesheet` | source_item_1d9b673c5ea4acd6 | جمع دارایی‌های سریع  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_b910306441f8195c` | `source-items-balancesheet` | source_item_b910306441f8195c | جمع دارایی‌های غیر جاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_e004c16278468e2e` | `source-items-balancesheet` | source_item_e004c16278468e2e | جمع کل دارایی‌ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_275d695812e56d3c` | `source-items-balancesheet` | source_item_275d695812e56d3c | جمع بدهی‌های جاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_9913d943244bfb8e` | `source-items-balancesheet` | source_item_9913d943244bfb8e | جمع بدهی‌های غیر جاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_27c04be7d889c619` | `source-items-balancesheet` | source_item_27c04be7d889c619 | جمع بدهی‌های کوتاه مدت بهره دار | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_11ce4200c55b7794` | `source-items-balancesheet` | source_item_11ce4200c55b7794 | جمع بدهی‌های بلندمدت بهره‌دار | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_7995f61fff6d4d76` | `source-items-balancesheet` | source_item_7995f61fff6d4d76 | جمع کل بدهی‌ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_57a818e843d0302a` | `source-items-balancesheet` | source_item_57a818e843d0302a | جمع کل بدهی‌های بهره‌دار  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_576f053343e4a839` | `source-items-balancesheet` | source_item_576f053343e4a839 | خالص سرمایه در گردش  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_b53fbc840d8b77fb` | `source-items-balancesheet` | source_item_b53fbc840d8b77fb | جمع اندوخته‌ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a639aa0beaf991b6` | `source-items-balancesheet` | source_item_a639aa0beaf991b6 | جمع حقوق صاحبان سهام | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_20ed167db4808821` | `source-items-balancesheet` | source_item_20ed167db4808821 | جمع بدهی‌ها و حقوق صاحبان سهام  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1f54faf005d92c84` | `source-items-balancesheet` | source_item_1f54faf005d92c84 | جمع دارایی‌های آنی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_9e755ac6ed8fed32` | `source-items-balancesheet` | source_item_9e755ac6ed8fed32 | جمع حقوق صاحبان سهام شرکت اصلی  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_0235c538a7a6c17c` | `source-items-balancesheet` | source_item_0235c538a7a6c17c | سهام خزانه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_15a63a8672b95632` | `source-items-balancesheet` | source_item_15a63a8672b95632 | سایر ذخایر  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_678f9161a2b38f18` | `source-items-balancesheet` | source_item_678f9161a2b38f18 | سرمایه پرداخت شده  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_066c1b25930f8aa3` | `source-items-balancesheet` | source_item_066c1b25930f8aa3 | سهم بیمه‌گران اتکایی از ذخایر | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_c4e281294f127c25` | `source-items-balancesheet` | source_item_c4e281294f127c25 | ناخالص ذخایر فنی  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_5d44c23e2571e16c` | `source-items-balancesheet` | source_item_5d44c23e2571e16c | مطالبات از بیمه‌گران اتکایی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1d2fcd11907ea08f` | `source-items-balancesheet` | source_item_1d2fcd11907ea08f | بدهی به بیمه‌گران اتکایی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_631cf83884419d9c` | `source-items-balancesheet` | source_item_631cf83884419d9c | خالص ذخایر فنی بیمه‌ای | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_bb2dafc2e014f9d6` | `source-items-balancesheet` | source_item_bb2dafc2e014f9d6 | مجموع سرمایه گذاری های صورت پذیرفته | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_5d369a336b943d17` | `source-items-balancesheet` | source_item_5d369a336b943d17 | مجموع سپرده های جذب شده | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_e00735cb3412cf4a` | `source-items-balancesheet` | source_item_e00735cb3412cf4a | تسهیلات اعطایی به بانکها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4112a654604b9fde` | `source-items-balancesheet` | source_item_4112a654604b9fde | مجموع دارایی های درآمد زا | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1cd260925e16744e` | `source-items-balancesheet` | source_item_1cd260925e16744e | دارایی های غیر جاری نگهداری شده برای فروش | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_b7a94b81aac15038` | `source-items-balancesheet` | source_item_b7a94b81aac15038 | مجموع دارایی های عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3ae0cbbb2d7c2d41` | `source-items-balancesheet` | source_item_3ae0cbbb2d7c2d41 | ارزش دفتری هر سهم | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_50b6f1f74c2fb786` | `source-items-balancesheet` | source_item_50b6f1f74c2fb786 | نوع دوره مالی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_01c6e38c515769b6` | `source-items-balancesheet` | source_item_01c6e38c515769b6 | بدهیهای مرتبط با داراییهای غیر جاری نگهداری شده برای فروش | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_26cef2607eeed716` | `source-items-balancesheet` | source_item_26cef2607eeed716 | مجموع تسهیلات اعطایی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_13f1735d6d2cbec3` | `source-items-balancesheet` | source_item_13f1735d6d2cbec3 | مجموع دریافتنی های تجاری و غیر تجاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_0e34488388e0c5ea` | `source-items-balancesheet` | source_item_0e34488388e0c5ea | مجموع بدهی های کوتاه مدت غیر بهره دار | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2877867ecd1d6c75` | `source-items-balancesheet` | source_item_2877867ecd1d6c75 | مجموع بدهی های بلند مدت غیر بهره دار | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_cf37972726512dcb` | `source-items-balancesheet` | source_item_cf37972726512dcb | جمع کل بدهی های غیر بهره دار | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3edbb0979ad4e9b4` | `source-items-balancesheet` | source_item_3edbb0979ad4e9b4 | صرف سهام خزانه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2f2caa3efc0c5dbc` | `source-items-balancesheet` | source_item_2f2caa3efc0c5dbc | جمع دارایی‌های جاری - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_601a78db848a9b63` | `source-items-balancesheet` | source_item_601a78db848a9b63 | جمع دارایی‌های غیر جاری - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_94f3b6d84163e462` | `source-items-balancesheet` | source_item_94f3b6d84163e462 | جمع کل دارایی‌ها - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_e9ed87e9889a0e61` | `source-items-balancesheet` | source_item_e9ed87e9889a0e61 | جمع بدهی‌های جاری - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3b72b531130e4192` | `source-items-balancesheet` | source_item_3b72b531130e4192 | جمع بدهی‌های غیر جاری - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_b59da420c69beaf5` | `source-items-balancesheet` | source_item_b59da420c69beaf5 | جمع کل بدهی‌ها - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_db853e88c3c1cebe` | `source-items-balancesheet` | source_item_db853e88c3c1cebe | جمع حقوق صاحبان سهام - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4febfe2c007a23a6` | `source-items-balancesheet` | source_item_4febfe2c007a23a6 | جمع بدهی‌ها و حقوق صاحبان سهام - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_bfcfce3d50af101d` | `source-items-balancesheet` | source_item_bfcfce3d50af101d | دریافتنی های تجاری و سایر دریافتنی ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_41d0f608e1e2a4cc` | `source-items-balancesheet` | source_item_41d0f608e1e2a4cc | پرداختنی های تجاری و سایر پرداختنی ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_775aea6d72b691ac` | `source-items-balancesheet` | source_item_775aea6d72b691ac | مجموع پرداختنی های تجاری و غیر تجاری  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_407fcec253a8930e` | `source-items-balancesheet` | source_item_407fcec253a8930e | جمع دارایی عملیاتی جاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_18b93d56f97a0b25` | `source-items-balancesheet` | source_item_18b93d56f97a0b25 | جمع دارایی عملیاتی غیر جاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_eebc1eb48e37fe5a` | `source-items-balancesheet` | source_item_eebc1eb48e37fe5a | آثار معاملات با منافع فاقد حق کنترل | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_bdb0d714a909bfd0` | `source-items-balancesheet` | source_item_bdb0d714a909bfd0 | دارایی مالیات انتقالی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_7a0f5390452e7c1a` | `source-items-balancesheet` | source_item_7a0f5390452e7c1a | بدهی مالیات انتقالی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_86072b96f2aef077` | `source-items-balancesheet` | source_item_86072b96f2aef077 | آثار معاملات سهام خزانه شرکت فرعی | — | pending-review | approved-direct-source-identity |
| `source-items-cashflow` | `financial-statement-source-items` | CashFlow | CashFlow | — | pending-review | not-authorized |
| `direct-source-item-source_item_1c2f7825c7662eda` | `source-items-cashflow` | source_item_1c2f7825c7662eda | جریان وجه نقد عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_bb281ab326f0afe7` | `source-items-cashflow` | source_item_bb281ab326f0afe7 | سود دریافتی بابت سپرده های سرمایه گذاری و اوراق مشارکت بلند مدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_8d523b5211f9630a` | `source-items-cashflow` | source_item_8d523b5211f9630a | سود سهام دریافتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_785108aac669ad80` | `source-items-cashflow` | source_item_785108aac669ad80 | سود پرداختی بابت تسهیلات مالی/ پیش دریافت ها و سایر حساب های پرداختنی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_cd47ce58a7da34e5` | `source-items-cashflow` | source_item_cd47ce58a7da34e5 | سود سهام پرداختی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1c89d174676ed3e6` | `source-items-cashflow` | source_item_1c89d174676ed3e6 | مالیات بر درآمد پرداختی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_233efda600c4303a` | `source-items-cashflow` | source_item_233efda600c4303a | وجوه پرداختی بابت خرید دارایی‌های ثابت مشهود | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_236a3f61815a4ca0` | `source-items-cashflow` | source_item_236a3f61815a4ca0 | وجوه پرداختی جهت خرید سرمایه‌گذاری‌های بلندمدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_054787f2fc8a80dc` | `source-items-cashflow` | source_item_054787f2fc8a80dc | وجوه پرداختی بابت تحصیل سرمایه‌گذاری‌های کوتاه مدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a27a124d737a95f9` | `source-items-cashflow` | source_item_a27a124d737a95f9 | وجوه پرداختی جهت خرید سایر دارایی‌ها و موجودی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_62560c5e286d872c` | `source-items-cashflow` | source_item_62560c5e286d872c | وجوه حاصل از فروش دارایی های ثابت مشهود | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1f6695052697c933` | `source-items-cashflow` | source_item_1f6695052697c933 | وجوه حاصل از فروش سرمایه گذاری بلند مدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a6abfdb2f448df18` | `source-items-cashflow` | source_item_a6abfdb2f448df18 | وجوه حاصل از فروش سرمایه گذاری های کوتاه مدت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_6a55bc9c64281a89` | `source-items-cashflow` | source_item_6a55bc9c64281a89 | وجوه دریافتی حاصل از افزایش سرمایه/حق تقدم | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_b657404c7e81b370` | `source-items-cashflow` | source_item_b657404c7e81b370 | وجوه حاصل از افزایش سرمایه شرکت های فرعی - سهم اقلیت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_282ee70d82c15813` | `source-items-cashflow` | source_item_282ee70d82c15813 | دریافت تسهیلات مالی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_d377b35724e6be92` | `source-items-cashflow` | source_item_d377b35724e6be92 | بازپرداخت اصل تسهیلات مالی دریافتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_012f88dd37b1ac2c` | `source-items-cashflow` | source_item_012f88dd37b1ac2c | مانده وجه نقد در آغاز سال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2236cd1228414671` | `source-items-cashflow` | source_item_2236cd1228414671 | تاثیر تغییرات نرخ ارز | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_960a2901dbc2646b` | `source-items-cashflow` | source_item_960a2901dbc2646b | سود پرداختی بابت اوراق مشارکت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_275c57d4e8779a76` | `source-items-cashflow` | source_item_275c57d4e8779a76 | وجوه دریافت شده بابت انتشار اوراق مشارکت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1c8f21d9881f4f13` | `source-items-cashflow` | source_item_1c8f21d9881f4f13 | باز پرداخت اصل اوراق مشارکت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_5d098503cd4b1ad4` | `source-items-cashflow` | source_item_5d098503cd4b1ad4 | سود دریافتی بابت تسهیلات مالی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_cbb8d764299a0bb6` | `source-items-cashflow` | source_item_cbb8d764299a0bb6 | وجوه پرداختی جهت خرید دارایی زیستی مولد | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_b24d6c8a23deb984` | `source-items-cashflow` | source_item_b24d6c8a23deb984 | وجوه حاصل از فروش دارایی زیستی مولد | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_7a6963d1b7ce8056` | `source-items-cashflow` | source_item_7a6963d1b7ce8056 | وجوه پرداختی بابت تحصیل دارایی های نامشهود | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_9d9f884bb51654c3` | `source-items-cashflow` | source_item_9d9f884bb51654c3 | جریان وجه نقد بازده سرمایه گذاریها و سود پرداختی بابت تامین مالی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_01cb0f14c99ae3eb` | `source-items-cashflow` | source_item_01cb0f14c99ae3eb | جریان وجه نقد سرمایه گذاری - قدیم | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_c1db587fefc33ea6` | `source-items-cashflow` | source_item_c1db587fefc33ea6 | جریان وجه نقد قبل از فعالیت های تامین مالی  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_8f5a4cc5a2ba6a85` | `source-items-cashflow` | source_item_8f5a4cc5a2ba6a85 | جریان وجه نقد تامین مالی - قدیم | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_f5871336613f0f05` | `source-items-cashflow` | source_item_f5871336613f0f05 | خالص افزایش (کاهش) در وجه نقد | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_e3c400252fd144d3` | `source-items-cashflow` | source_item_e3c400252fd144d3 | مانده وجه نقد در پایان سال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_74eb490c7e6d4494` | `source-items-cashflow` | source_item_74eb490c7e6d4494 | وجوه پرداختی جهت تحصیل شرکتهای فرعی و وابسته | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_19d120dae1813e95` | `source-items-cashflow` | source_item_19d120dae1813e95 | وجوه دریافتی بابت فروش شرکتهای فرعی و وابسته | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_ad3bee04283a29af` | `source-items-cashflow` | source_item_ad3bee04283a29af | وجوه دریافتی بابت فروش دارایی های نامشهود | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_76593b5031a564bf` | `source-items-cashflow` | source_item_76593b5031a564bf | وام نقدی پرداختی به سایر اشخاص | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_c2736fb9c66d591f` | `source-items-cashflow` | source_item_c2736fb9c66d591f | وصول (اقساط / اصل) وام نقدی پرداختی به سایر اشخاص | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_28bd251a1acbc84e` | `source-items-cashflow` | source_item_28bd251a1acbc84e | وجوه پرداختی بابت خرید دارایی های غیر جاری نگهداری شده برای فروش | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2c52eaac007758da` | `source-items-cashflow` | source_item_2c52eaac007758da | وجوه دریافتی حاصل از فروش دارایی های غیر جاری نگهداری شده برای فروش | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2e1a104c7f34c014` | `source-items-cashflow` | source_item_2e1a104c7f34c014 | مجموع منابع بازده سرمایه گذاری ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_0bae12e01c190350` | `source-items-cashflow` | source_item_0bae12e01c190350 | مجموع مصارف بازده سرمایه گذاری ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2d8879faef125dad` | `source-items-cashflow` | source_item_2d8879faef125dad | مجموع منابع سرمایه گذاری ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a8078f7b57710af2` | `source-items-cashflow` | source_item_a8078f7b57710af2 | مجموع مصارف سرمایه گذاری ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_f5252ddd609b06f0` | `source-items-cashflow` | source_item_f5252ddd609b06f0 | مجموع منابع تامین مالی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_632832e8500c17fb` | `source-items-cashflow` | source_item_632832e8500c17fb | مجموع مصارف تامین مالی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1bbe9d1b9ec721e9` | `source-items-cashflow` | source_item_1bbe9d1b9ec721e9 | مجموع جریانات نقد ورودی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a4bb7eb20f341ddb` | `source-items-cashflow` | source_item_a4bb7eb20f341ddb | مجموع جریانات نقد خروجی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4b038beea7732cc1` | `source-items-cashflow` | source_item_4b038beea7732cc1 | بازپرداخت اوراق اجاره (صکوک) / اجاره سرمایه ای | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_6adfc052d5ed0b90` | `source-items-cashflow` | source_item_6adfc052d5ed0b90 | وجوه پرداختی بابت خرید سهام خزانه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_726b4355c61f1ef1` | `source-items-cashflow` | source_item_726b4355c61f1ef1 | وجوه دریافتی حاصل از فروش سهام خزانه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_fb97c87778cb39f3` | `source-items-cashflow` | source_item_fb97c87778cb39f3 | جریان وجه نقد بازده سرمایه گذاریها و سود پرداختی بابت تامین مالی - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1a59249d6ddd6efa` | `source-items-cashflow` | source_item_1a59249d6ddd6efa | جریان وجه نقد سرمایه گذاری - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_e2757a36c84e2554` | `source-items-cashflow` | source_item_e2757a36c84e2554 | جریان وجه نقد قبل از فعالیت های تامین مالی - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3721b19d2b88396d` | `source-items-cashflow` | source_item_3721b19d2b88396d | جریان وجه نقد تامین مالی - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_d449d193ce441baf` | `source-items-cashflow` | source_item_d449d193ce441baf | خالص افزایش (کاهش) در وجه نقد - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_f9d70a42de6adfd9` | `source-items-cashflow` | source_item_f9d70a42de6adfd9 | مانده وجه نقد در پایان سال - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1132fc8f9caa9104` | `source-items-cashflow` | source_item_1132fc8f9caa9104 | جریان وجه نقد سرمایه گذاری - جدید | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_10f2d3a4209eaa0c` | `source-items-cashflow` | source_item_10f2d3a4209eaa0c | جریان وجه نقد تامین مالی - جدید | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3d034f5e00145631` | `source-items-cashflow` | source_item_3d034f5e00145631 | جریان وجه نقد عملیاتی - جدید | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_c29cd7e583393138` | `source-items-cashflow` | source_item_c29cd7e583393138 | دریافت های نقدی حاصل از فروش سرمایه گذاری در املاک | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_e627b2e29d3bec5a` | `source-items-cashflow` | source_item_e627b2e29d3bec5a | پرداخت های نقدی برای تحصیل سرمایه گذاری در املاک | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_289e2fa68d342b27` | `source-items-cashflow` | source_item_289e2fa68d342b27 | دریافت های نقدی حاصل از صرف سهام | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_62dad1dba36ed159` | `source-items-cashflow` | source_item_62dad1dba36ed159 | دریافت های نقدی حاصل از انتشار اوراق خرید دین | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_f82a7d2ce51064bb` | `source-items-cashflow` | source_item_f82a7d2ce51064bb | پرداخت های نقدی بابت اصل اوراق خرید دین | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_bfc054edc2eae474` | `source-items-cashflow` | source_item_bfc054edc2eae474 | پرداخت های نقدی بابت سود اوراق خرید دین | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_dcdc26507f32e88d` | `source-items-cashflow` | source_item_dcdc26507f32e88d | پرداخت های نقدی بابت سود اجاره سرمایه‌ای | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_7675d39e9d517704` | `source-items-cashflow` | source_item_7675d39e9d517704 | پرداخت‌های نقدی ناشی از تحصیل منافع فاقد حق کنترل | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_91e5756221514234` | `source-items-cashflow` | source_item_91e5756221514234 | دریافت‌های نقدی ناشی از واگذاری بخشی از شرکت فرعی - با حفظ کنترل | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_7e94a34b77105b54` | `source-items-cashflow` | source_item_7e94a34b77105b54 | دریافت‌های نقدی ناشی از فروش سهام خزانه توسط شرکت فرعی- سهم منافع فاقد حق کنترل | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_07821f7b846a62f4` | `source-items-cashflow` | source_item_07821f7b846a62f4 | پرداخت‌های نقدی برای خرید سهام خزانه توسط شرکت فرعی- سهم منافع فاقد حق کنترل | — | pending-review | approved-direct-source-identity |
| `source-items-incomestatement` | `financial-statement-source-items` | IncomeStatement | IncomeStatement | — | pending-review | not-authorized |
| `direct-source-item-source_item_6558bad9b71d190c` | `source-items-incomestatement` | source_item_6558bad9b71d190c | بهای تمام شده کالای فروش رفته و خدمات ارائه شده | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_99f1bf68e37cbd01` | `source-items-incomestatement` | source_item_99f1bf68e37cbd01 | هزینه های فروش، اداری و عمومی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_088d754abc0f33df` | `source-items-incomestatement` | source_item_088d754abc0f33df | هزینه مطالبات مشکوک الوصول | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_cf6a13435fa928af` | `source-items-incomestatement` | source_item_cf6a13435fa928af | کارمزد پرداختی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_c3c0540b9de8f519` | `source-items-incomestatement` | source_item_c3c0540b9de8f519 | هزینه استهلاک | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_f15a14b5db06246e` | `source-items-incomestatement` | source_item_f15a14b5db06246e | هزینه‌های مالی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_456a8755eb720ae3` | `source-items-incomestatement` | source_item_456a8755eb720ae3 | مالیات بر درآمد | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_d67346d7f0e8cfd6` | `source-items-incomestatement` | source_item_d67346d7f0e8cfd6 | فروش خالص و درآمد ارایه خدمات | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1042da31aa3eafb9` | `source-items-incomestatement` | source_item_1042da31aa3eafb9 | درآمد حاصل از سپرده گذاری‌ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_c4bcaf2a87a828ca` | `source-items-incomestatement` | source_item_c4bcaf2a87a828ca | کارمزد دریافتی و درآمد حق عاملیت | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4fd6301dd1f72a0d` | `source-items-incomestatement` | source_item_4fd6301dd1f72a0d | نتیجه معاملات ارزی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_200b5c14544989de` | `source-items-incomestatement` | source_item_200b5c14544989de | سود (زیان) انباشته در ابتدای سال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_8e56a1487d621edb` | `source-items-incomestatement` | source_item_8e56a1487d621edb | تعدیلات سنواتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_fa937452f018129f` | `source-items-incomestatement` | source_item_fa937452f018129f | سود سهام مصوب سال قبل-قبل از تخصیص | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_31ffc9828726bcc4` | `source-items-incomestatement` | source_item_31ffc9828726bcc4 | اندوخته قانونی-تخصیص داده شده  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_174acd6cf096ca33` | `source-items-incomestatement` | source_item_174acd6cf096ca33 | سایر اندوخته‌ها-تخصیص داده شده | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_206c6d96f0cd9fce` | `source-items-incomestatement` | source_item_206c6d96f0cd9fce | خالص سایر درآمدها و هزینه های عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_8245c60a2143c004` | `source-items-incomestatement` | source_item_8245c60a2143c004 | خالص سایر درآمدها و هزینه‌های غیر عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_afb9d0cb6c1a89ed` | `source-items-incomestatement` | source_item_afb9d0cb6c1a89ed | سود دریافتی بابت تسهیلات عقود اسلامی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_00804b44699af7ed` | `source-items-incomestatement` | source_item_00804b44699af7ed | سهم اقلیت افشایی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_e19a3ba97cccddcf` | `source-items-incomestatement` | source_item_e19a3ba97cccddcf | سود(زیان) /درآمد حاصل از سرمایه گذاری ها-غیر عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_918e8bac627af21f` | `source-items-incomestatement` | source_item_918e8bac627af21f | انتقال به حساب سرمایه قبل از تخصیص سود | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_769e91f3f2afb2c7` | `source-items-incomestatement` | source_item_769e91f3f2afb2c7 | خالص سود (زیان) حاصل از فروش سرمایه گذاری ها  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_f24f15adb4fae4a4` | `source-items-incomestatement` | source_item_f24f15adb4fae4a4 | جایزه سپرده قانونی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_181de80afb54086a` | `source-items-incomestatement` | source_item_181de80afb54086a | سهم گروه از سود (زیان) شرکت های وابسته - غیر عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_d5bcb0ca4b7c88fe` | `source-items-incomestatement` | source_item_d5bcb0ca4b7c88fe | حق بیمه صادره | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_dbf10cf391f8e166` | `source-items-incomestatement` | source_item_dbf10cf391f8e166 | خسارت سهم بیمه گران اتکایی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_159bd5c5f78637f6` | `source-items-incomestatement` | source_item_159bd5c5f78637f6 | هزینه کارمزد و تمبر | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_ea0f15f465e94239` | `source-items-incomestatement` | source_item_ea0f15f465e94239 | خسارت پرداختی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_917a5eedbd770b54` | `source-items-incomestatement` | source_item_917a5eedbd770b54 | سایر درآمد (هزینه) ‌های بیمه‌ای | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_672b534b3a08ebb4` | `source-items-incomestatement` | source_item_672b534b3a08ebb4 | سهم اقلیت محاسباتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4b8c88c34da6651f` | `source-items-incomestatement` | source_item_4b8c88c34da6651f | تعدیلات تلفیق | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_393011d1d840c5b8` | `source-items-incomestatement` | source_item_393011d1d840c5b8 | سود ناخالص | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_9c617846fc7c5c13` | `source-items-incomestatement` | source_item_9c617846fc7c5c13 | سود عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_63dadf34cf4d7943` | `source-items-incomestatement` | source_item_63dadf34cf4d7943 | سود قبل از احتساب سهم گروه از سود (زیان) شرکت های وابسته | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_21e50b17f6027495` | `source-items-incomestatement` | source_item_21e50b17f6027495 | سود ناشی از فعالیت های عادی قبل از مالیات | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_66fdf1090cbf96d3` | `source-items-incomestatement` | source_item_66fdf1090cbf96d3 | سود خالص | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3927b279fdb9ffac` | `source-items-incomestatement` | source_item_3927b279fdb9ffac | سود خالص قابل انتساب به صاحبان سهام شرکت اصلی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a4e20dbf00331785` | `source-items-incomestatement` | source_item_a4e20dbf00331785 | سود (زیان) انباشته در ابتدای سال- تعدیل شده | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_9a399e6d405b3f1f` | `source-items-incomestatement` | source_item_9a399e6d405b3f1f | سود (زیان) انباشته ابتدای دوره تخصیص نیافته | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_aa392e4ba22df75d` | `source-items-incomestatement` | source_item_aa392e4ba22df75d | سود تخصیص داده شده طی سال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3a0a1391cda2f082` | `source-items-incomestatement` | source_item_3a0a1391cda2f082 | سود (زیان) انباشته در پایان سال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_62ae56b73d4c0668` | `source-items-incomestatement` | source_item_62ae56b73d4c0668 | سود قبل از کسر اقلام غیر مترقبه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_031f8d8d8772e58b` | `source-items-incomestatement` | source_item_031f8d8d8772e58b | جمع درآمد های مشاع | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_98fcfc5697f14dd9` | `source-items-incomestatement` | source_item_98fcfc5697f14dd9 | جمع درآمد های غیر مشاع  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_cb33a67a0a14c39b` | `source-items-incomestatement` | source_item_cb33a67a0a14c39b | سهم سود بانک از درآمد های مشاع (شامل حق الوکاله) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_01df66012ef44b51` | `source-items-incomestatement` | source_item_01df66012ef44b51 | تعداد سهام شرکت - به هزار سهم  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_bb4b63de3c1ecfe9` | `source-items-incomestatement` | source_item_bb4b63de3c1ecfe9 | سود خالص هر سهم  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_682beceadddc1458` | `source-items-incomestatement` | source_item_682beceadddc1458 | سود عملیاتی هر سهم  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_8d15f4167c173d2f` | `source-items-incomestatement` | source_item_8d15f4167c173d2f | سود هر سهم قبل از کسر مالیات  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_05336a8f3103ed0c` | `source-items-incomestatement` | source_item_05336a8f3103ed0c | حصه هر سهم از جمع درآمدها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_e4d7143da0555fac` | `source-items-incomestatement` | source_item_e4d7143da0555fac | سود (زیان) فروش دارایی‌های زیستی مولد  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1cc36f72de4983fe` | `source-items-incomestatement` | source_item_1cc36f72de4983fe | سود خالص تلفیقی هر سهم | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_43a29cf8ee0d9dbc` | `source-items-incomestatement` | source_item_43a29cf8ee0d9dbc | جمع درآمدهای بیمه ای | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_ce0f91f7d743880b` | `source-items-incomestatement` | source_item_ce0f91f7d743880b | جمع هزینه های بیمه ای | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_6731ce65f5fb0c55` | `source-items-incomestatement` | source_item_6731ce65f5fb0c55 | سود ناخالص بیمه ای | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3719c4db2805b889` | `source-items-incomestatement` | source_item_3719c4db2805b889 | تغییر در ذخایر  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_6fdd85f5781a09bb` | `source-items-incomestatement` | source_item_6fdd85f5781a09bb | حق بیمه اتکائی واگذاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_48177c1e1f40d632` | `source-items-incomestatement` | source_item_48177c1e1f40d632 | سود علی الحساب سپرده‌های سرمایه‌گذاری مدت دار | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_02ed8f95fe9d4281` | `source-items-incomestatement` | source_item_02ed8f95fe9d4281 | سود و وجه التزام دریافتی غیر مشاع | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1f68ccb847613d6d` | `source-items-incomestatement` | source_item_1f68ccb847613d6d | خالص سایر درآمدهای غیر مشاع | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_71712bc79b5fb76f` | `source-items-incomestatement` | source_item_71712bc79b5fb76f | بهای تمام شده خدمات ارائه شده شرکت‌های وابسته و فرعی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_cb0f52c4e689cdf4` | `source-items-incomestatement` | source_item_cb0f52c4e689cdf4 | درآمد حاصل از خدمات ارائه شده شرکت‌های وابسته و فرعی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_218f767285079eaf` | `source-items-incomestatement` | source_item_218f767285079eaf | سود (زیان) حاصل از خدمات ارائه شده | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_b0e48d078f4742e0` | `source-items-incomestatement` | source_item_b0e48d078f4742e0 | جمع درآمدهای بانک و درآمد عملیاتی شرکت ها | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_6d57fe6b0711807c` | `source-items-incomestatement` | source_item_6d57fe6b0711807c | جمع هزینه های عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_52661e81a9efb798` | `source-items-incomestatement` | source_item_52661e81a9efb798 | حصه هر سهم از جمع درآمدهای مشاع | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2d608bb1eea5985c` | `source-items-incomestatement` | source_item_2d608bb1eea5985c | سود (زیان) سرمایه گذاریها از محل ذخائر فنی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_59259d1bc12f9a32` | `source-items-incomestatement` | source_item_59259d1bc12f9a32 | سود (زیان) سرمایه گذاریها از محل سایر منابع  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_c5987f346df74dab` | `source-items-incomestatement` | source_item_c5987f346df74dab | درآمد حق بیمه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a75b39e0eb1819c0` | `source-items-incomestatement` | source_item_a75b39e0eb1819c0 | هزینه حق بیمه واگذاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_0df05004fed05b09` | `source-items-incomestatement` | source_item_0df05004fed05b09 | درآمد حق بیمه سهم نگهداری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_715e5009ab5e324f` | `source-items-incomestatement` | source_item_715e5009ab5e324f | هزینه خسارت  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_11aac82a4a40ee7c` | `source-items-incomestatement` | source_item_11aac82a4a40ee7c | خالص خسارت سهم بیمه گران اتکایی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3190956964d76b27` | `source-items-incomestatement` | source_item_3190956964d76b27 | هزینه خسارت سهم نگهداری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_8f6bda294ebdfcf2` | `source-items-incomestatement` | source_item_8f6bda294ebdfcf2 | هزینه کارمزد و کارمزد منافع سهم نگهداری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_699ab207216a922b` | `source-items-incomestatement` | source_item_699ab207216a922b | سود عملیات بیمه ای | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_7cd0ef73a29f29a3` | `source-items-incomestatement` | source_item_7cd0ef73a29f29a3 | درآمد سرمایه گذاریها از محل ذخائر فنی و سایر منابع | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_0cb71bf4da8f041e` | `source-items-incomestatement` | source_item_0cb71bf4da8f041e | ذخیره کاهش ارزش سرمایه گذاریها از محل ذخائر فنی و سایر منابع | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_af7925152f5132d3` | `source-items-incomestatement` | source_item_af7925152f5132d3 | سود حاصل از سپرده گذاریها و سرمایه گذاریها و اوراق مشارکت غیر مشاع | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3d8cf93537c4d51f` | `source-items-incomestatement` | source_item_3d8cf93537c4d51f | افزایش(کاهش) سایر ذخایر فنی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3fcaeb0d8b3a5fee` | `source-items-incomestatement` | source_item_3fcaeb0d8b3a5fee | سود (زیان) عملیاتی قبل از اقلام استثنائی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_cb9fa3f5f81a6b9a` | `source-items-incomestatement` | source_item_cb9fa3f5f81a6b9a | سود (زیان) / درآمد حاصل از سرمایه گذاری ها  | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_de317ad9d8956d03` | `source-items-incomestatement` | source_item_de317ad9d8956d03 | جمع هزینه های بانک | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_dbd8c0cb206aa955` | `source-items-incomestatement` | source_item_dbd8c0cb206aa955 | جمع درآمدهای مشاع و غیر مشاع | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_47afc8c13569c0cb` | `source-items-incomestatement` | source_item_47afc8c13569c0cb | درآمدهای عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_9979766a3b118e0b` | `source-items-incomestatement` | source_item_9979766a3b118e0b | هزینه های عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_f22c23fabe7558ee` | `source-items-incomestatement` | source_item_f22c23fabe7558ee | سود خالص عملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_d90624e0b8a8d0c5` | `source-items-incomestatement` | source_item_d90624e0b8a8d0c5 | اندوخته قانونی محاسباتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3d15d79aa8387abc` | `source-items-incomestatement` | source_item_3d15d79aa8387abc | مجموع اندوخته | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_b6fa83fafd1bc1e2` | `source-items-incomestatement` | source_item_b6fa83fafd1bc1e2 | سود (زیان) عملیات متوقف شده | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_bdad50f2d95056c8` | `source-items-incomestatement` | source_item_bdad50f2d95056c8 | سود (زیان) عملیات در حال تداوم | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_8a8fbcd62f58ab31` | `source-items-incomestatement` | source_item_8a8fbcd62f58ab31 | درآمد | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_1a872ca8064e59f9` | `source-items-incomestatement` | source_item_1a872ca8064e59f9 | درآمد تسهیلات اعطایی و سپرده گذاری (بانک مرکزی) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_725d4c49a3961581` | `source-items-incomestatement` | source_item_725d4c49a3961581 | هزینه سود سپرده ها (بانک مرکزی) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a800619e9e54cb6d` | `source-items-incomestatement` | source_item_a800619e9e54cb6d | خالص درآمد تسهیلات و سپرده گذاری (بانک مرکزی) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_723a50380c1a1f89` | `source-items-incomestatement` | source_item_723a50380c1a1f89 | خالص درآمد کارمزد (بانک مرکزی) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_29b992e5d363388c` | `source-items-incomestatement` | source_item_29b992e5d363388c | جمع سایر درآمدهای عملیاتی (بانک مرکزی) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_eb9b2327ccf250ae` | `source-items-incomestatement` | source_item_eb9b2327ccf250ae | سود ناخالص - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_ab6fb50b0326e481` | `source-items-incomestatement` | source_item_ab6fb50b0326e481 | سود عملیاتی - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_88384c3e81d6e41b` | `source-items-incomestatement` | source_item_88384c3e81d6e41b | سود ناشی از فعالیت های عادی قبل از مالیات - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_35fdf5c842acb3e7` | `source-items-incomestatement` | source_item_35fdf5c842acb3e7 | سود خالص - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_7098f4fba69689d7` | `source-items-incomestatement` | source_item_7098f4fba69689d7 | سود (زیان) انباشته در ابتدای سال- تعدیل شده - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_35014704369d1269` | `source-items-incomestatement` | source_item_35014704369d1269 | سود قابل تخصیص - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_629945cd7f474145` | `source-items-incomestatement` | source_item_629945cd7f474145 | سود (زیان) انباشته در پایان سال - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_e7aa8c86fb41bc7d` | `source-items-incomestatement` | source_item_e7aa8c86fb41bc7d | سود خالص هر سهم - کدال | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_3efad950165ec3b8` | `source-items-incomestatement` | source_item_3efad950165ec3b8 | سود قبل از بهره و مالیات (EBIT) | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_cc02058e76e15826` | `source-items-incomestatement` | source_item_cc02058e76e15826 | سود قابل تخصیص | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_08acf52be4638ae0` | `source-items-incomestatement` | source_item_08acf52be4638ae0 | سود (زیان) انباشته قابل انتساب به صاحبان سهام شرکت اصلی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_4f487efcc018057b` | `source-items-incomestatement` | source_item_4f487efcc018057b | انتقال از سایر اقلام حقوق صاحبان سهام | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2af0b7ac9d357c29` | `source-items-incomestatement` | source_item_2af0b7ac9d357c29 | مالیات سال جاری | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_2d1032f36fcc7fba` | `source-items-incomestatement` | source_item_2d1032f36fcc7fba | مالیات سال های قبل | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_89112f81d0b0cb06` | `source-items-incomestatement` | source_item_89112f81d0b0cb06 | مجموع مالیات بر درآمد | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_aa05a44246225074` | `source-items-incomestatement` | source_item_aa05a44246225074 | مغایرت گردش سود و زیان انباشته | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_de7a1248aba284b8` | `source-items-incomestatement` | source_item_de7a1248aba284b8 | فروش سهام خزانه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_6babd30ae97be50b` | `source-items-incomestatement` | source_item_6babd30ae97be50b | هزینه مشارکت در منافع | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_a0924e75a509d026` | `source-items-incomestatement` | source_item_a0924e75a509d026 | خرید سهام خزانه | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_24a425d0b87561a2` | `source-items-incomestatement` | source_item_24a425d0b87561a2 | سهم گروه از سود مشارکت های خاص - غیرعملیاتی | — | pending-review | approved-direct-source-identity |
| `direct-source-item-source_item_01978c436d9ca89e` | `source-items-incomestatement` | source_item_01978c436d9ca89e | سود (زیان) حاصل از فروش سهام خزانه | — | pending-review | approved-direct-source-identity |
