
-- Threads
CREATE TABLE public.threads (
  id UUID NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
  user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
  title TEXT NOT NULL DEFAULT 'گفتگوی جدید',
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_threads_user ON public.threads(user_id, updated_at DESC);
ALTER TABLE public.threads ENABLE ROW LEVEL SECURITY;
CREATE POLICY "threads_select_own" ON public.threads FOR SELECT USING (auth.uid() = user_id);
CREATE POLICY "threads_insert_own" ON public.threads FOR INSERT WITH CHECK (auth.uid() = user_id);
CREATE POLICY "threads_update_own" ON public.threads FOR UPDATE USING (auth.uid() = user_id);
CREATE POLICY "threads_delete_own" ON public.threads FOR DELETE USING (auth.uid() = user_id);

-- Messages
CREATE TABLE public.messages (
  id UUID NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
  thread_id UUID NOT NULL REFERENCES public.threads(id) ON DELETE CASCADE,
  user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
  role TEXT NOT NULL CHECK (role IN ('user','assistant')),
  content JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_messages_thread ON public.messages(thread_id, created_at ASC);
ALTER TABLE public.messages ENABLE ROW LEVEL SECURITY;
CREATE POLICY "messages_select_own" ON public.messages FOR SELECT USING (auth.uid() = user_id);
CREATE POLICY "messages_insert_own" ON public.messages FOR INSERT WITH CHECK (auth.uid() = user_id);
CREATE POLICY "messages_delete_own" ON public.messages FOR DELETE USING (auth.uid() = user_id);

-- Watchlists
CREATE TABLE public.watchlists (
  user_id UUID NOT NULL PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
  symbols TEXT[] NOT NULL DEFAULT ARRAY['فملی','شستا','کگل','توسن']::TEXT[],
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
ALTER TABLE public.watchlists ENABLE ROW LEVEL SECURITY;
CREATE POLICY "watchlists_select_own" ON public.watchlists FOR SELECT USING (auth.uid() = user_id);
CREATE POLICY "watchlists_insert_own" ON public.watchlists FOR INSERT WITH CHECK (auth.uid() = user_id);
CREATE POLICY "watchlists_update_own" ON public.watchlists FOR UPDATE USING (auth.uid() = user_id);

-- Subscriptions
CREATE TABLE public.user_subscriptions (
  user_id UUID NOT NULL PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
  plan TEXT NOT NULL DEFAULT 'pro' CHECK (plan IN ('free','pro','plus','premium')),
  ai_credits_remaining INT NOT NULL DEFAULT 840,
  ai_credits_total INT NOT NULL DEFAULT 1000,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
ALTER TABLE public.user_subscriptions ENABLE ROW LEVEL SECURITY;
CREATE POLICY "subs_select_own" ON public.user_subscriptions FOR SELECT USING (auth.uid() = user_id);
CREATE POLICY "subs_insert_own" ON public.user_subscriptions FOR INSERT WITH CHECK (auth.uid() = user_id);
CREATE POLICY "subs_update_own" ON public.user_subscriptions FOR UPDATE USING (auth.uid() = user_id);

-- Auto-create subscription + watchlist on signup
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  INSERT INTO public.user_subscriptions (user_id) VALUES (NEW.id) ON CONFLICT DO NOTHING;
  INSERT INTO public.watchlists (user_id) VALUES (NEW.id) ON CONFLICT DO NOTHING;
  RETURN NEW;
END;
$$;

CREATE TRIGGER on_auth_user_created
AFTER INSERT ON auth.users
FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();

-- Update threads.updated_at when messages added
CREATE OR REPLACE FUNCTION public.touch_thread()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
  UPDATE public.threads SET updated_at = now() WHERE id = NEW.thread_id;
  RETURN NEW;
END;
$$;
CREATE TRIGGER trg_touch_thread AFTER INSERT ON public.messages
FOR EACH ROW EXECUTE FUNCTION public.touch_thread();
