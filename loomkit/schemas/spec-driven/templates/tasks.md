## 1. <!-- Task Group Name -->

### Task 1: <!-- Component Name -->

**Files:**
- Create: `exact/path/to/file.ts`
- Modify: `exact/path/to/existing.ts:line-range`
- Test: `tests/exact/path/to/test.ts`

- [ ] **Step 1: Write the failing test**

```typescript
test('specific behavior', () => {
  // test code
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `vitest run tests/path/test.ts --reporter=verbose`
Expected: FAIL

- [ ] **Step 3: Write minimal implementation**

```typescript
// implementation code
```

- [ ] **Step 4: Run test to verify it passes**

Run: `vitest run tests/path/test.ts --reporter=verbose`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add tests/path/test.ts src/path/file.ts
git commit -m "feat: specific feature"
```
