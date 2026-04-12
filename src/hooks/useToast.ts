import { useState, useRef, useCallback, useMemo } from "react";
import { ICONS } from "../utils/icons";
import type { ToastLogEntry, ToastType } from "../types";

interface ToastState {
  message: string;
  type: ToastType;
  visible: boolean;
}

const MAX_TOAST_SUMMARY = 120;
const MAX_LOG_ITEMS = 100;

function compactWhitespace(value: string) {
  return value.replace(/\s+/g, " ").trim();
}

function splitIconPrefix(message: string) {
  const compact = message.trimStart();
  const icon = Object.values(ICONS).find(token => compact.startsWith(token));
  if (!icon) return { iconPrefix: "", text: compact };
  return { iconPrefix: `${icon} `, text: compact.slice(icon.length).trimStart() };
}

function buildToastSummary(message: string) {
  const compact = compactWhitespace(message);
  const { iconPrefix, text } = splitIconPrefix(compact);

  const looksDetailed = text.includes("|") || text.includes("{") || text.length > MAX_TOAST_SUMMARY;
  if (!looksDetailed) return compact;

  const head = compactWhitespace(text.split("|")[0] ?? text);
  const shortHead = head.length > 84 ? `${head.slice(0, 83)}...` : head;
  return `${iconPrefix}${shortHead} Veja detalhes no sininho.`;
}

export const useToast = () => {
  const [state, setState] = useState<ToastState>({ message: "", type: "success", visible: false });
  const [logs, setLogs] = useState<ToastLogEntry[]>([]);
  const timerRef = useRef<ReturnType<typeof setTimeout>>(null);

  const unreadCount = useMemo(
    () => logs.reduce((count, item) => count + (item.read ? 0 : 1), 0),
    [logs]
  );

  const show = useCallback((message: string, type: ToastType = "success") => {
    if (timerRef.current) clearTimeout(timerRef.current);

    const summary = buildToastSummary(message);
    setState({ message: summary, type, visible: true });

    const entry: ToastLogEntry = {
      id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      summary,
      fullMessage: compactWhitespace(message),
      type,
      createdAt: new Date().toISOString(),
      read: false,
    };

    setLogs(previous => [entry, ...previous].slice(0, MAX_LOG_ITEMS));
    timerRef.current = setTimeout(() => setState(previous => ({ ...previous, visible: false })), 3500);
  }, []);

  const markAllRead = useCallback(() => {
    setLogs(previous => previous.map(item => (item.read ? item : { ...item, read: true })));
  }, []);

  const clearLogs = useCallback(() => {
    setLogs([]);
  }, []);

  return {
    toast: state,
    show,
    logs,
    unreadCount,
    markAllRead,
    clearLogs,
  };
};
