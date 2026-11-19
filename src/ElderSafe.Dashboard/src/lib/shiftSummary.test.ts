import { describe, expect, it } from "vitest";
import { splitCitations } from "@/lib/shiftSummary";

describe("splitCitations", () => {
  it("REQ-AI-01 turns each cited alert reference into a citation", () => {
    expect(splitCitations("[A1] was answered; [A2] escalated.", ["A1", "A2"])).toEqual([
      { kind: "citation", ref: "A1" },
      { kind: "text", text: " was answered; " },
      { kind: "citation", ref: "A2" },
      { kind: "text", text: " escalated." },
    ]);
  });

  it("REQ-AI-01 also turns a reference written without brackets into a citation", () => {
    expect(splitCitations("There is one alert, A1, still open.", ["A1"])).toEqual([
      { kind: "text", text: "There is one alert, " },
      { kind: "citation", ref: "A1" },
      { kind: "text", text: ", still open." },
    ]);
  });

  it("REQ-AI-01 leaves a reference that is not one of the summary's alerts as plain text", () => {
    expect(splitCitations("See [A9].", ["A1"])).toEqual([{ kind: "text", text: "See [A9]." }]);
  });

  it("REQ-AI-01 returns a summary with no citations as one piece of text", () => {
    expect(splitCitations("No alerts were raised.", [])).toEqual([
      { kind: "text", text: "No alerts were raised." },
    ]);
  });
});
