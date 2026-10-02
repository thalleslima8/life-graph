import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Button } from "./button";

describe("Button", () => {
  it("renders a native button by default", () => {
    render(<Button>Salvar</Button>);

    expect(screen.getByRole("button", { name: "Salvar" })).toBeInTheDocument();
  });

  it("renders its child element instead when asChild is set", () => {
    render(
      <Button asChild>
        <a href="/inicio">Início</a>
      </Button>,
    );

    expect(screen.getByRole("link", { name: "Início" })).toHaveAttribute("href", "/inicio");
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
});
