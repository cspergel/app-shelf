import * as React from "react";
import { cn } from "@/lib/utils";

const Card = React.forwardRef<
  HTMLDivElement,
  React.HTMLAttributes<HTMLDivElement>
>(({ className, ...props }, ref) => (
  <div
    ref={ref}
    className={cn(
      "rounded-card border border-hairline bg-surface-card shadow-[0_1px_3px_rgba(0,0,0,0.45)] transition-all duration-150 hover:border-hairline/80 hover:bg-surface-card-hover hover:-translate-y-px hover:shadow-[0_6px_20px_rgba(0,0,0,0.5)]",
      className,
    )}
    {...props}
  />
));
Card.displayName = "Card";

const CardContent = React.forwardRef<
  HTMLDivElement,
  React.HTMLAttributes<HTMLDivElement>
>(({ className, ...props }, ref) => (
  <div ref={ref} className={cn("p-4", className)} {...props} />
));
CardContent.displayName = "CardContent";

export { Card, CardContent };
