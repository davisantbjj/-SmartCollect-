export type ToastType = "success" | "info" | "warn" | "error";

export type ShowToast = (message: string, type?: ToastType) => void;
