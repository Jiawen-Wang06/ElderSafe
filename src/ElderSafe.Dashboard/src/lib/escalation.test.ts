import { describe, expect, it } from "vitest";
import { selectableEscalationLimits } from "@/lib/escalation";

describe("selectableEscalationLimits", () => {
  it("REQ-CFG-09 offers one option per assigned caregiver", () => {
    expect(selectableEscalationLimits(3)).toEqual([1, 2, 3]);
  });

  it("REQ-CFG-09 caps at ten even with more caregivers assigned", () => {
    expect(selectableEscalationLimits(12)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  });

  it("REQ-CFG-09 always offers at least one option", () => {
    expect(selectableEscalationLimits(0)).toEqual([1]);
  });
});
