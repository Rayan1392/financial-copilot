import { createServerFn } from "@tanstack/react-start";
import { z } from "zod";
import { requireSupabaseAuth } from "@/integrations/supabase/auth-middleware";
import { supabaseAdmin } from "@/integrations/supabase/client.server";
import { generateMockReply, generateThreadTitle, type ChatBlock } from "./mock-data.server";

// List threads for the signed-in user.
export const listThreads = createServerFn({ method: "GET" })
  .middleware([requireSupabaseAuth])
  .handler(async ({ context }) => {
    const { supabase } = context;
    const { data, error } = await supabase
      .from("threads")
      .select("id, title, updated_at, created_at")
      .order("updated_at", { ascending: false });
    if (error) throw new Error(error.message);
    return data ?? [];
  });

// Create a new thread.
export const createThread = createServerFn({ method: "POST" })
  .middleware([requireSupabaseAuth])
  .handler(async ({ context }) => {
    const { supabase, userId } = context;
    const { data, error } = await supabase
      .from("threads")
      .insert({ user_id: userId, title: "گفتگوی جدید" })
      .select("id, title, updated_at, created_at")
      .single();
    if (error) throw new Error(error.message);
    return data;
  });

// Load messages for a thread.
export const getThreadMessages = createServerFn({ method: "POST" })
  .middleware([requireSupabaseAuth])
  .inputValidator((d) => z.object({ threadId: z.string().uuid() }).parse(d))
  .handler(async ({ context, data }) => {
    const { supabase } = context;
    const { data: rows, error } = await supabase
      .from("messages")
      .select("id, role, content, created_at")
      .eq("thread_id", data.threadId)
      .order("created_at", { ascending: true });
    if (error) throw new Error(error.message);
    return rows ?? [];
  });

// Delete a thread.
export const deleteThread = createServerFn({ method: "POST" })
  .middleware([requireSupabaseAuth])
  .inputValidator((d) => z.object({ threadId: z.string().uuid() }).parse(d))
  .handler(async ({ context, data }) => {
    const { supabase } = context;
    const { error } = await supabase.from("threads").delete().eq("id", data.threadId);
    if (error) throw new Error(error.message);
    return { ok: true };
  });

// Send a user message and get a mocked AI reply. Persists both.
export const sendChatMessage = createServerFn({ method: "POST" })
  .middleware([requireSupabaseAuth])
  .inputValidator((d) =>
    z
      .object({
        threadId: z.string().uuid(),
        message: z.string().min(1).max(2000),
        deepResearch: z.boolean().optional(),
      })
      .parse(d),
  )
  .handler(async ({ context, data }) => {
    const { supabase, userId } = context;

    // Save user message
    const userContent = { text: data.message };
    const { data: userMsg, error: userErr } = await supabase
      .from("messages")
      .insert({
        thread_id: data.threadId,
        user_id: userId,
        role: "user",
        content: userContent,
      })
      .select("id, role, content, created_at")
      .single();
    if (userErr) throw new Error(userErr.message);

    // Generate mock AI reply
    const reply: ChatBlock = generateMockReply(data.message, !!data.deepResearch);

    const { data: aiMsg, error: aiErr } = await supabase
      .from("messages")
      .insert({
        thread_id: data.threadId,
        user_id: userId,
        role: "assistant",
        content: reply as never,
      })
      .select("id, role, content, created_at")
      .single();
    if (aiErr) throw new Error(aiErr.message);

    // Update thread title from first user message if still default
    const { data: thread } = await supabase
      .from("threads")
      .select("title")
      .eq("id", data.threadId)
      .single();
    if (thread?.title === "گفتگوی جدید") {
      await supabase
        .from("threads")
        .update({ title: generateThreadTitle(data.message) })
        .eq("id", data.threadId);
    }

    // Decrement credits (server-side only via admin client; RLS forbids user updates)
    const { data: sub } = await supabaseAdmin
      .from("user_subscriptions")
      .select("ai_credits_remaining")
      .eq("user_id", userId)
      .single();
    if (sub) {
      await supabaseAdmin
        .from("user_subscriptions")
        .update({
          ai_credits_remaining: Math.max(0, sub.ai_credits_remaining - reply.creditsUsed),
        })
        .eq("user_id", userId);
    }

    return { userMsg, aiMsg };
  });

// Subscription
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
      // Backfill if trigger didn't fire (e.g. legacy users)
      const { data: created } = await supabase
        .from("user_subscriptions")
        .insert({ user_id: userId })
        .select("plan, ai_credits_remaining, ai_credits_total")
        .single();
      return created!;
    }
    return data;
  });

// Watchlist
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
