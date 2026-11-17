import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path

_SCRIPT = Path(__file__).resolve().parent.parent / "verify-solid-contracts.py"
_SPEC = importlib.util.spec_from_file_location("verify_solid_contracts", _SCRIPT)
verify_solid_contracts = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(verify_solid_contracts)


def _git(repo: Path, *args: str) -> None:
    subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True, text=True)


class DiscoverChangedFilesTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.repo = Path(self._tmp.name)
        _git(self.repo, "init", "-q")
        _git(self.repo, "config", "user.email", "test@example.invalid")
        _git(self.repo, "config", "user.name", "test")
        _git(self.repo, "config", "status.showUntrackedFiles", "normal")
        (self.repo / "existing.txt").write_text("base\n", encoding="utf-8")
        _git(self.repo, "add", "existing.txt")
        _git(self.repo, "commit", "-q", "-m", "base")

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def test_lists_every_file_inside_a_new_untracked_folder(self) -> None:
        nested = self.repo / "tools" / "NewProject" / "Sub"
        nested.mkdir(parents=True)
        (self.repo / "tools" / "NewProject" / "Program.cs").write_text("class P {}\n", encoding="utf-8")
        (nested / "Mapper.cs").write_text("class M {}\n", encoding="utf-8")

        files = verify_solid_contracts.discover_changed_files(self.repo, None)

        self.assertIn("tools/NewProject/Program.cs", files)
        self.assertIn("tools/NewProject/Sub/Mapper.cs", files)
        self.assertNotIn("tools/", files)
        self.assertNotIn("tools/NewProject/", files)

    def test_lists_modified_tracked_files(self) -> None:
        (self.repo / "existing.txt").write_text("changed\n", encoding="utf-8")

        files = verify_solid_contracts.discover_changed_files(self.repo, None)

        self.assertEqual(["existing.txt"], files)


if __name__ == "__main__":
    unittest.main()
