import os
import shutil
import uuid
import re

def generate_guid():
    return uuid.uuid4().hex

def duplicate_folder(src_dir, dst_dir):
    if os.path.exists(dst_dir):
        shutil.rmtree(dst_dir)
    shutil.copytree(src_dir, dst_dir)

    # 1. Collect all old GUIDs and generate new ones
    guid_map = {}
    meta_files = []
    
    for root, dirs, files in os.walk(dst_dir):
        for file in files:
            if file.endswith('.meta'):
                meta_path = os.path.join(root, file)
                meta_files.append(meta_path)
                with open(meta_path, 'r', encoding='utf-8') as f:
                    content = f.read()
                    
                match = re.search(r'guid:\s*([a-f0-9]{32})', content)
                if match:
                    old_guid = match.group(1)
                    new_guid = generate_guid()
                    guid_map[old_guid] = new_guid

    # 2. Replace all occurrences of old GUIDs with new GUIDs in all text-based files
    text_extensions = {'.meta', '.unity', '.prefab', '.mat', '.asset', '.controller', '.anim'}
    
    for root, dirs, files in os.walk(dst_dir):
        for file in files:
            ext = os.path.splitext(file)[1].lower()
            if ext in text_extensions or file.endswith('.meta'):
                file_path = os.path.join(root, file)
                try:
                    with open(file_path, 'r', encoding='utf-8') as f:
                        content = f.read()
                    
                    modified = False
                    for old_guid, new_guid in guid_map.items():
                        if old_guid in content:
                            content = content.replace(old_guid, new_guid)
                            modified = True
                            
                    if modified:
                        with open(file_path, 'w', encoding='utf-8', newline='') as f:
                            f.write(content)
                except Exception as e:
                    pass

    print(f"Duplicated {src_dir} to {dst_dir}")
    print(f"Remapped {len(guid_map)} GUIDs to ensure independence.")

if __name__ == '__main__':
    src = r"e:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine\Assets\MassGPUPhysics"
    dst = r"e:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine\Assets\MassGPUPhysics_Stage2"
    duplicate_folder(src, dst)
