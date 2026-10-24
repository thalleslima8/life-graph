import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/**
 * A name with case and accents folded, so "Livro" and "lívro" match. Used only to match and
 * suggest; the server decides.
 */
export function nameKeyOf(name: string): string {
  return name.normalize("NFD").replace(/[̀-ͯ]/g, "").toLocaleLowerCase("pt-BR");
}
