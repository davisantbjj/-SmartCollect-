export type ToastType = "success" | "info" | "warn" | "error";

export type ShowToast = (message: string, type?: ToastType) => void;

export interface ToastLogEntry {
	id: string;
	summary: string;
	fullMessage: string;
	type: ToastType;
	createdAt: string;
	read: boolean;
}
