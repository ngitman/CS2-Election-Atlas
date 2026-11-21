// Minimal type shims for the game's UI modules, covering only what this mod uses.
// Prop names were checked against the game's own UI bundle (game 1.6.4).
// The modding toolchain ships full typings; switch to those once it is installed.

declare module "cs2/modding" {
  import type { ComponentType } from "react";
  export interface ModuleRegistry {
    append(location: string, component: ComponentType<any>): void;
  }
  export type ModRegistrar = (moduleRegistry: ModuleRegistry) => void;
}

declare module "cs2/api" {
  export interface ValueBinding<T> {
    readonly value: T;
  }
  export function bindValue<T>(group: string, name: string, fallbackValue?: T): ValueBinding<T>;
  export function trigger(group: string, name: string, ...args: unknown[]): void;
  export function useValue<T>(binding: ValueBinding<T>): T;
}

declare module "cs2/ui" {
  import type { CSSProperties, ReactNode } from "react";

  export interface PanelProps {
    header?: ReactNode;
    footer?: ReactNode;
    draggable?: boolean;
    initialPosition?: { x: number; y: number };
    onClose?: () => void;
    className?: string;
    style?: CSSProperties;
    children?: ReactNode;
  }
  export const Panel: (props: PanelProps) => JSX.Element;

  export interface FloatingButtonProps {
    src?: string;
    selected?: boolean;
    onSelect?: () => void;
    className?: string;
  }
  export const FloatingButton: (props: FloatingButtonProps) => JSX.Element;
}
