import { useState } from "react";
import { CalendarDays } from "lucide-react";
import { faIRJalali } from "react-day-picker/locale/fa-IR-jalali";
import { getDateLib } from "react-day-picker/persian";
import { Calendar } from "@/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import {
  formatDisclosureDateOnly,
  parseDisclosureDateOnly,
  serializeDisclosureDateOnly,
} from "@/lib/format/disclosure";

const jalaliDateLib = getDateLib({ locale: faIRJalali, numerals: "arabext" });

/**
 * A Jalali calendar-day picker. The value is the selected day as `YYYY-MM-DD` (empty when none); moved unchanged out of
 * the disclosures page so the admin conversation logs (Feature 164) use the same control.
 */
export function JalaliDateFilter({
  label,
  ariaLabel,
  value,
  onChange,
}: {
  label: string;
  ariaLabel: string;
  value: string;
  onChange: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = parseDisclosureDateOnly(value);
  return (
    <div className="space-y-1 text-xs text-muted-foreground">
      <span className="block">{label}</span>
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <button
            type="button"
            aria-label={ariaLabel}
            aria-haspopup="dialog"
            className="flex w-full items-center justify-between gap-2 rounded-xl border border-hairline bg-background px-3 py-2.5 text-sm text-foreground outline-none transition hover:border-emerald/35 focus-visible:border-emerald/50 focus-visible:ring-2 focus-visible:ring-emerald/10"
          >
            <span className={selected ? "" : "text-muted-foreground"}>
              {selected ? formatDisclosureDateOnly(value) : "انتخاب تاریخ"}
            </span>
            <CalendarDays className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          </button>
        </PopoverTrigger>
        <PopoverContent align="start" dir="rtl" className="w-auto p-2">
          <Calendar
            mode="single"
            selected={selected}
            onSelect={(date) => {
              onChange(date ? serializeDisclosureDateOnly(date) : "");
              setOpen(false);
            }}
            locale={faIRJalali}
            dateLib={jalaliDateLib}
            numerals="arabext"
            dir="rtl"
            initialFocus
          />
          {selected && (
            <button
              type="button"
              onClick={() => {
                onChange("");
                setOpen(false);
              }}
              className="w-full rounded-md px-2 py-1.5 text-xs text-muted-foreground transition hover:bg-accent hover:text-foreground"
            >
              حذف تاریخ
            </button>
          )}
        </PopoverContent>
      </Popover>
    </div>
  );
}
