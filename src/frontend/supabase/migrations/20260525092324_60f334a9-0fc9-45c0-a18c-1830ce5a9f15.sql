
CREATE OR REPLACE FUNCTION public.touch_thread()
RETURNS TRIGGER LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
BEGIN
  UPDATE public.threads SET updated_at = now() WHERE id = NEW.thread_id;
  RETURN NEW;
END;
$$;

REVOKE EXECUTE ON FUNCTION public.handle_new_user() FROM PUBLIC, anon, authenticated;
REVOKE EXECUTE ON FUNCTION public.touch_thread() FROM PUBLIC, anon, authenticated;
