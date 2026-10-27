"""A stand-in for the few parts of pytest that the generated tests use (fixture, skip, mark.parametrize), so they can be played without installing pytest. Not a replacement."""
import inspect

_fixtures = {}


class Skipped(Exception):
    pass


def skip(reason=""):
    raise Skipped(reason)


def fixture(function=None, scope="function"):
    def register(f):
        _fixtures[f.__name__] = (f, scope)
        return f

    return register(function) if function is not None else register


class _Mark:
    def parametrize(self, names, values):
        def apply(f):
            f.__params__ = (names, values)
            return f

        return apply


mark = _Mark()


def run_module(module):
    cache, closers, results = {}, [], []

    def resolve(name):
        function, scope = _fixtures[name]
        if scope == "session" and name in cache:
            return cache[name]
        args = [resolve(p) for p in inspect.signature(function).parameters]
        value = function(*args)
        if inspect.isgenerator(value):
            generator = value
            value = next(generator)
            closers.append((scope, generator))
        if scope == "session":
            cache[name] = value
        return value

    def close(scope):
        for entry in [c for c in closers if c[0] == scope]:
            closers.remove(entry)
            try:
                next(entry[1])
            except StopIteration:
                pass

    for name, test in [(n, f) for n, f in vars(module).items() if n.startswith("test_") and callable(f)]:
        cases = [{}]
        if hasattr(test, "__params__"):
            names, values = test.__params__
            cases = [{names: v} for v in values]
        for case in cases:
            label = name + (f"[{list(case.values())[0]}]" if case else "")
            try:
                kwargs = {p: case[p] if p in case else resolve(p) for p in inspect.signature(test).parameters}
                test(**kwargs)
                results.append((label, "passed", ""))
            except Skipped as skipped:
                results.append((label, "skipped", str(skipped)))
            except Exception as error:  # noqa: BLE001
                results.append((label, "FAILED", repr(error)))
            finally:
                close("function")
    close("session")
    return results
