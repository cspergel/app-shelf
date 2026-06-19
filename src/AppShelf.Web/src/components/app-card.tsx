import { Play, Square, Star, ExternalLink } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { StatusDot, statusMeta } from "./status-dot";
import { cn } from "@/lib/utils";
import type { AppView } from "@/lib/types";

interface AppCardProps {
  app: AppView;
  onLaunch: (id: string) => void;
  onStop: (id: string) => void;
}

export function AppCard({ app, onLaunch, onStop }: AppCardProps) {
  const meta = statusMeta(app.status);
  const isStopped = app.status === "Stopped";
  const isRunning = app.status === "Running";

  return (
    <Card className="group w-full">
      <CardContent className="flex flex-col gap-3">
        {/* Header: status dot, name, favorite, framework badge */}
        <div className="flex items-center gap-2.5">
          <StatusDot status={app.status} />
          <span className="flex-1 truncate text-sm font-semibold tracking-[-0.01em] text-text-primary">
            {app.name}
          </span>
          <Star
            className={cn(
              "h-3.5 w-3.5 shrink-0 transition-opacity",
              app.favorite
                ? "fill-[#E5C04B] text-[#E5C04B] opacity-100"
                : "text-text-faint opacity-0 group-hover:opacity-60",
            )}
          />
          {app.framework && (
            <Badge className="shrink-0">{app.framework}</Badge>
          )}
        </div>

        {/* Meta row: url + status label */}
        <div className="flex items-center justify-between gap-2">
          <span className="truncate font-mono text-[11px] text-text-secondary">
            {app.url}
          </span>
          <span className={cn("shrink-0 text-[11px] font-medium", meta.text)}>
            {meta.label}
          </span>
        </div>

        {/* Actions */}
        <div className="flex items-center gap-2 pt-0.5">
          {isStopped ? (
            <Button
              variant="primary"
              size="sm"
              onClick={() => onLaunch(app.id)}
            >
              <Play className="h-3.5 w-3.5" />
              Launch
            </Button>
          ) : isRunning ? (
            <Button
              variant="outline"
              size="sm"
              onClick={() => window.open(app.url, "_blank")}
            >
              <ExternalLink className="h-3.5 w-3.5" />
              Open
            </Button>
          ) : null}

          {!isStopped && (
            <Button variant="ghost" size="sm" onClick={() => onStop(app.id)}>
              <Square className="h-3 w-3" />
              Stop
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
