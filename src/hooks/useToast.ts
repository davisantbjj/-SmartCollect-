import { useState, useRef, useCallback } from "react";
import type { ToastType } from "../types";

interface ToastState {
  message: string;
  type: ToastType;
  visible: boolean;
}

export const useToast = () => {
  const [state, setState] = useState<ToastState>({ message: "", type: "success", visible: false });
  const timerRef = useRef<ReturnType<typeof setTimeout>>(null);
  const show = useCallback((message: string, type: ToastType = "success") => {
    if (timerRef.current) clearTimeout(timerRef.current);
    setState({ message, type, visible: true });
    timerRef.current = setTimeout(() => setState(previous => ({ ...previous, visible: false })), 3500);
  }, []);
  return { toast: state, show };
};
