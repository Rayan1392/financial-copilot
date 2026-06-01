import { createServerFn } from "@tanstack/react-start";
import { requireSupabaseAuth } from "@/integrations/supabase/auth-middleware";

// Temporary prototype reads retained until spec 033 replaces sidebar usage and watchlist data.
export const getSubscription = createServerFn({ method: "GET" })
  .middleware([requireSupabaseAuth])
  .handler(async ({ context }) => {
    const { supabase, userId } = context;
    const { data, error } = await supabase
      .from("user_subscriptions")
      .select("plan, ai_credits_remaining, ai_credits_total")
      .eq("user_id", userId)
      .maybeSingle();
    if (error) throw new Error(error.message);
    if (!data) {
      const { data: created } = await supabase
        .from("user_subscriptions")
        .insert({ user_id: userId })
        .select("plan, ai_credits_remaining, ai_credits_total")
        .single();
      return created!;
    }
    return data;
  });

export const getWatchlist = createServerFn({ method: "GET" })
  .middleware([requireSupabaseAuth])
  .handler(async ({ context }) => {
    const { supabase, userId } = context;
    const { data, error } = await supabase
      .from("watchlists")
      .select("symbols")
      .eq("user_id", userId)
      .maybeSingle();
    if (error) throw new Error(error.message);
    if (!data) {
      const { data: created } = await supabase
        .from("watchlists")
        .insert({ user_id: userId })
        .select("symbols")
        .single();
      return created!.symbols;
    }
    return data.symbols;
  });
