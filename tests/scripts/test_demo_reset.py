"""Run with python3 -m unittest discover -s tests/scripts."""

import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class DemoResetOpeningValuesTests(unittest.TestCase):
    def test_reset_selects_management_policy_and_disables_password_reset(self):
        reset = (ROOT / "scripts/demo-reset.sh").read_text()
        helpers = reset[reset.index("step() {"):reset.index('step "Stop the stack"')]
        opening_values = reset[reset.index('step "Restore opening values"'):reset.index('step "Check prerequisites"')]
        for initial_policy in ("DenyManagement", "CanManagePlaces"):
            with self.subTest(initial_policy=initial_policy), tempfile.TemporaryDirectory() as directory:
                realm = Path(directory) / "realm.yaml"
                policy = Path(directory) / "PlaceEndpoints.cs"
                realm.write_text((ROOT / "infra/keycloak/config/city-picks.yaml").read_text().replace(
                    "resetPasswordAllowed: false", "resetPasswordAllowed: true"))
                policy.write_text(f'public const string ManagementPolicy = "{initial_policy}";\n')
                harness = 'realm_yaml="$1"; policy_file="$2"; warnings=()\n'
                # Repeat the real reset section to verify that the opening state is stable.
                subprocess.run(
                    ["bash", "-euc", harness + helpers + opening_values + opening_values,
                     "reset-test", str(realm), str(policy)],
                    capture_output=True, text=True, check=True,
                )
                self.assertIn("resetPasswordAllowed: false\n", realm.read_text())
                self.assertIn('ManagementPolicy = "CanManagePlaces";', policy.read_text())


class DemoResetPrerequisitesTests(unittest.TestCase):
    def check_images(self, missing_mailpit=False):
        prepare = (ROOT / "scripts/prepare.sh").read_text()
        reset = (ROOT / "scripts/demo-reset.sh").read_text()
        prepared_images = re.findall(r"^docker pull (\S+)$", prepare, re.MULTILINE)
        if missing_mailpit:
            prepared_images = [image for image in prepared_images if "mailpit" not in image]

        # Exercise the real prerequisite loop without stopping the demo or resetting files.
        image_check = re.search(r"^for image in .*?^done$", reset, re.MULTILINE | re.DOTALL)[0]
        harness = '''
docker() {
  [[ "$1 $2" == "image inspect" ]] || return 2
  local image
  for image in $PREPARED_IMAGES; do
    [[ "$3" == "$image" ]] && return 0
  done
  return 1
}
ok() { printf 'ok: %s\\n' "$1"; }
warn() { printf 'WARNING: %s\\n' "$1"; }
'''
        result = subprocess.run(
            ["bash", "-c", harness + image_check],
            env={**os.environ, "PREPARED_IMAGES": " ".join(prepared_images)},
            capture_output=True, text=True, check=True,
        )
        return result.stdout

    def test_prepared_images_are_accepted_without_tag_aliases(self):
        self.assertNotIn("not pulled", self.check_images())

    def test_missing_mailpit_still_warns(self):
        output = self.check_images(missing_mailpit=True)
        self.assertIn("WARNING: image axllent/mailpit:", output)
        self.assertIn("not pulled: run ./scripts/prepare.sh while online", output)
