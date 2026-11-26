"""Fetch daily prices for the sample symbols from the public TSETMC API into data/05_tsetmc_prices.csv.

The local Sapio database holds daily trades for only four of the seven symbols and stops at 2026-11-21,
so the reference-date price (1405/09/03 = 2026-11-24) comes from TSETMC. Only public instrument codes are sent.
"""
import csv, json, pathlib, subprocess, sys

sys.stdout.reconfigure(encoding="utf-8")

INSTRUMENTS = {  # Companies.InstrumentCode in the Sapio database
    "اخشان": "67868738676326187", "فاذر": "38547060135156069", "ارفع": "59266699437480384",
    "اخابر": "22811176775480091", "شگل": "44153164692325703", "کگهر": "30703140537034664",
    "خزامیا": "2589887561569709", "فملی": "35425587644337450", "فولاد": "46348559193224090",
    "کگل": "35700344742885862",
}
out = pathlib.Path(__file__).parent / "data" / "05_tsetmc_prices.csv"
with out.open("w", newline="", encoding="utf-8") as fh:
    w = csv.writer(fh)
    w.writerow(["ticker", "ins_code", "date", "closing_price", "last_price", "price_yesterday", "volume"])
    for ticker, code in INSTRUMENTS.items():
        url = f"https://cdn.tsetmc.com/api/ClosingPrice/GetClosingPriceDailyList/{code}/130"
        # curl is used because Python's TLS handshake to this host times out on this machine.
        raw = subprocess.run(["curl", "-s", "-m", "40", "--retry", "3", "-A", "Mozilla/5.0", url],
                             capture_output=True, check=True).stdout
        days = json.loads(raw)["closingPriceDaily"]
        for d in sorted(days, key=lambda x: x["dEven"]):
            s = str(d["dEven"])
            w.writerow([ticker, code, f"{s[:4]}-{s[4:6]}-{s[6:]}", d["pClosing"], d["pDrCotVal"],
                        d["priceYesterday"], d["qTotTran5J"]])
        print(ticker, len(days), "days")
